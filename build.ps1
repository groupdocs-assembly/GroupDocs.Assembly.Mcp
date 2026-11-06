# Taken from psake https://github.com/psake/psake

<#
.SYNOPSIS
  This is a helper function that runs a scriptblock and checks the PS variable $lastexitcode
  to see if an error occurred. If an error is detected then an exception is thrown.
.EXAMPLE
  exec { dotnet build } "Error executing dotnet build"
#>
function Exec {
    [CmdletBinding()]
    param(
        [Parameter(Position = 0, Mandatory = 1)][scriptblock]$cmd,
        [Parameter(Position = 1, Mandatory = 0)][string]$errorMessage = ($msgs.error_bad_command -f $cmd)
    )
    & $cmd
    if ($lastexitcode -ne 0) {
        throw ("Exec: " + $errorMessage)
    }
}

if (Test-Path .\build_out) { Remove-Item .\build_out -Force -Recurse }

<#
.SYNOPSIS
  Verifies that `.mcp/server.json` version fields match the source of truth in
  `build/dependencies.props`. Fails the build on drift to prevent a release where
  the NuGet package version and the MCP server manifest disagree.
#>
function Assert-ServerJsonVersionMatchesDependencies {
    param(
        [Parameter(Mandatory)][string]$DepsPropsPath,
        [Parameter(Mandatory)][string]$ServerJsonPath,
        [Parameter(Mandatory)][string]$VersionPropertyName
    )

    if (-not (Test-Path $DepsPropsPath)) { throw "dependencies.props not found at $DepsPropsPath" }
    if (-not (Test-Path $ServerJsonPath)) { throw "server.json not found at $ServerJsonPath" }

    [xml]$depsXml = Get-Content $DepsPropsPath
    $expectedVersion = ($depsXml.SelectSingleNode("//$VersionPropertyName")).InnerText.Trim()
    if (-not $expectedVersion) { throw "Property <$VersionPropertyName> not found in $DepsPropsPath" }

    $serverJsonText = Get-Content $ServerJsonPath -Raw
    $pattern = '"version"\s*:\s*"' + [regex]::Escape($expectedVersion) + '"'
    $matchCount = [regex]::Matches($serverJsonText, $pattern).Count

    if ($matchCount -ne 2) {
        throw "server.json version mismatch: expected both top-level and packages[0] 'version' = '$expectedVersion' (from <$VersionPropertyName> in $DepsPropsPath). Found $matchCount occurrence(s). Update $ServerJsonPath and try again."
    }
    Write-Host "build: server.json version '$expectedVersion' matches dependencies.props"
}

Assert-ServerJsonVersionMatchesDependencies `
    -DepsPropsPath ".\build\dependencies.props" `
    -ServerJsonPath ".\src\GroupDocs.Assembly.Mcp\.mcp\server.json" `
    -VersionPropertyName "GroupDocsAssemblyMcp"

exec { & dotnet restore src\GroupDocs.Assembly.Mcp.sln }

$isProd = $env:BUILD_TYPE -eq "PROD"

if ($isProd) {
    Write-Host "build: PROD build - stable version (no suffix)"
    exec { & dotnet build src\GroupDocs.Assembly.Mcp.sln -c Release --verbosity quiet --nologo }
} else {
    $commitHash = $(git rev-parse --short HEAD)
    $buildSuffix = "local-$commitHash"
    Write-Host "build: DEV build - version suffix is $buildSuffix"
    exec { & dotnet build src\GroupDocs.Assembly.Mcp.sln -c Release --version-suffix=$buildSuffix --verbosity quiet --nologo }
}

$packArgs = @('-c', 'Release', '-o', '.\build_out', '--include-symbols', '-p:SymbolPackageFormat=snupkg', '--no-build')
if (-not $isProd) { $packArgs += "--version-suffix=$buildSuffix" }

exec { & dotnet pack .\src\GroupDocs.Assembly.Mcp\GroupDocs.Assembly.Mcp.csproj @packArgs }
