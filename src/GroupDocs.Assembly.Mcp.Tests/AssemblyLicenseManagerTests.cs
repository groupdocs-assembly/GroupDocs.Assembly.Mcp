using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using GroupDocs.Assembly.Mcp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GroupDocs.Assembly.Mcp.Tests;

public class AssemblyLicenseManagerTests
{
    private static AssemblyLicenseManager Create(McpConfig? config = null) =>
        new(Microsoft.Extensions.Options.Options.Create(config ?? new McpConfig()), NullLogger<LicenseManager>.Instance);

    [Fact]
    public void WithoutConfiguration_IsEvaluationMode()
    {
        // Explicit empty values beat any machine-level environment variables for this check.
        var manager = Create(new McpConfig { LicensePath = "", MeteredPublicKey = "", MeteredPrivateKey = "" });
        var previous = (Environment.GetEnvironmentVariable(LicenseManager.LicensePathVariable),
                        Environment.GetEnvironmentVariable(LicenseManager.PublicKeyVariable),
                        Environment.GetEnvironmentVariable(LicenseManager.PrivateKeyVariable));
        try
        {
            Environment.SetEnvironmentVariable(LicenseManager.LicensePathVariable, null);
            Environment.SetEnvironmentVariable(LicenseManager.PublicKeyVariable, null);
            Environment.SetEnvironmentVariable(LicenseManager.PrivateKeyVariable, null);

            manager.SetLicense();

            Assert.False(manager.IsLicensed);
            Assert.Equal(LicenseMode.Evaluation, manager.Mode);
            Assert.Null(manager.GetConsumption());
        }
        finally
        {
            Environment.SetEnvironmentVariable(LicenseManager.LicensePathVariable, previous.Item1);
            Environment.SetEnvironmentVariable(LicenseManager.PublicKeyVariable, previous.Item2);
            Environment.SetEnvironmentVariable(LicenseManager.PrivateKeyVariable, previous.Item3);
        }
    }

    [Fact]
    public void MissingLicenseFile_StaysInEvaluation_AndSaysWhy()
    {
        var manager = Create(new McpConfig { LicensePath = Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.lic") });

        var ex = Record.Exception(manager.SetLicense);

        Assert.Null(ex);
        Assert.False(manager.IsLicensed);
        Assert.Contains("not found", manager.Diagnostic);
    }

    [Fact]
    public void Engine_IsReportedAsGroupDocsAssembly()
    {
        var engine = Create().DescribeEngine();
        Assert.NotNull(engine);
        Assert.Equal("GroupDocs.Assembly", engine!.Value.Name);
        Assert.StartsWith("26.", engine.Value.Version);
    }
}
