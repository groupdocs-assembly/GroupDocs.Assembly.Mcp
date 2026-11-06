# Taken from psake https://github.com/psake/psake

<#
.SYNOPSIS
  Publishes signed NuGet packages to NuGet.org.
  Expects packages in .\artifacts folder (output of the signing step).
.EXAMPLE
  $env:NUGET_API_KEY_PROD = "your-api-key"
  .\publish-prod.ps1
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

if (-Not (Test-Path .\artifacts)) { throw ("Folder '.\artifacts' does not exist.") }

$packages = @(Get-ChildItem .\artifacts -Filter *.nupkg)
if ($packages.Count -eq 0) {
    throw "No .nupkg files found in .\artifacts - refusing to publish nothing. Check the signing step output."
}

Write-Host "Found $($packages.Count) package(s) to publish."

foreach ($pkg in $packages) {
    $package = $pkg.FullName
    Write-Host "Package $package will be published."

    exec { & dotnet nuget push $package --api-key "$env:NUGET_API_KEY_PROD" --source https://api.nuget.org/v3/index.json --skip-duplicate }
}
