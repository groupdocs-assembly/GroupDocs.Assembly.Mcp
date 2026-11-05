using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GroupDocs.Assembly.Mcp;

public class AssemblyLicenseManager : LicenseManager
{
    public AssemblyLicenseManager(IOptions<McpConfig> config, ILogger<LicenseManager> logger)
        : base(config, logger)
    {
    }

    // Identifies the engine in get_license_status. Without it the tool would report the
    // server's own version, because this class lives in the server assembly.
    protected override Type? EngineMarkerType => typeof(GroupDocs.Assembly.License);

    protected override void SetLicenseFromPath(string licensePath)
    {
        new GroupDocs.Assembly.License().SetLicense(licensePath);
    }

    protected override void SetMeteredKeyCore(string publicKey, string privateKey)
    {
        new GroupDocs.Assembly.Metered().SetMeteredKey(publicKey, privateKey);
    }

    protected override MeteredConsumption ReadConsumptionCore()
    {
        // Static on the engine and only meaningful once a metered key is applied - Core
        // guarantees this runs in metered mode only.
        return new MeteredConsumption
        {
            Quantity = GroupDocs.Assembly.Metered.GetConsumptionQuantity(),
            Credit = GroupDocs.Assembly.Metered.GetConsumptionCredit()
        };
    }
}
