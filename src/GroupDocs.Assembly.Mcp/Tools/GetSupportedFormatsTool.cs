using System.ComponentModel;
using System.Text.Json;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GroupDocs.Assembly.Mcp.Tools;

[McpServerToolType]
public static class GetSupportedFormatsTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "get_supported_formats", ReadOnly = true, Idempotent = true), Description(
        "Lists the template formats GroupDocs.Assembly fills, the formats each kind of template can be saved as, and the data formats it reads. " +
        "Call it when the user asks which formats are supported, or whether a template can be saved as PDF, DOCX, HTML or another format. " +
        "Returns JSON. Without 'file', the full catalog: { families: [{ family, templateFormats[], saveFormats[] }], dataFormats: [{ kind, extensions[], note }] }; " +
        "with 'file', the save targets of that template: { fileName, family, saveFormats[] }. " +
        "Extensions are lower-case without a dot; families are WordProcessing (including TXT, Markdown and HTML templates), Spreadsheet, Presentation and Email. " +
        "Do NOT pre-check whether files exist — just pass the filename the user provided; a file that cannot be found returns the list of available files instead. " +
        "On failure isError is set and the text starts with 'Supported-formats lookup failed for'.")]
    public static async Task<CallToolResult> GetSupportedFormats(
        IFileResolver resolver,
        ILicenseManager licenseManager,
        [Description("Optional template: return only the formats this template can be saved as")] FileInput? file = null)
    {
        if (file is null || AssembleDocumentTool.IsEmpty(file))
        {
            var catalog = new
            {
                families = AssemblyFormats.Families.Select(f => new
                {
                    family = f,
                    templateFormats = AssemblyFormats.TemplateFormats(f),
                    saveFormats = AssemblyFormats.SaveFormats(f)
                }),
                dataFormats = new object[]
                {
                    new { kind = AssemblyFormats.JsonData, extensions = new[] { "json" }, note = "An object's keys are the names the template uses; an array is named after the template's data source. Also accepted inline as 'dataJson'." },
                    new { kind = AssemblyFormats.XmlData, extensions = new[] { "xml" }, note = "Named after the template's data source." },
                    new { kind = AssemblyFormats.CsvData, extensions = new[] { "csv", "tsv" }, note = "First row is column names unless hasHeaders=false; delimiter set with 'csvDelimiter'." },
                    new { kind = AssemblyFormats.TableData, extensions = AssemblyFormats.TableDataExtensions, note = "A table (a worksheet for spreadsheets) of the document, chosen with 'dataTable'." },
                }
            };
            return ToolError.Success(JsonSerializer.Serialize(catalog, JsonOptions));
        }

        licenseManager.SetLicense();
        using var resolved = await resolver.ResolveAsync(file);
        try
        {
            var family = AssemblyFormats.FamilyOfExtension(Path.GetExtension(resolved.FileName))
                         ?? throw new ToolInputException($"'{resolved.FileName}' is not a template format GroupDocs.Assembly reads.");
            return ToolError.Success(JsonSerializer.Serialize(new
            {
                fileName = resolved.FileName,
                family,
                saveFormats = AssemblyFormats.SaveFormats(family)
            }, JsonOptions));
        }
        catch (Exception ex)
        {
            return ToolError.Failure("Supported-formats lookup", resolved.FileName, ex);
        }
    }
}
