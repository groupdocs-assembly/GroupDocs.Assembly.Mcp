using System.ComponentModel;
using System.Text.Json;
using GroupDocs.Assembly.Data;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GroupDocs.Assembly.Mcp.Tools;

[McpServerToolType]
public static class GetDocumentInfoTool
{
    private const int MaxTables = 20;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "get_document_info", ReadOnly = true, Idempotent = true), Description(
        "Inspects a file before generating documents with it, as a template, as data, or both: its template family, size, how many <<…>> tags it has " +
        "and the data names they use, which formats it can be saved to, and — for a data file — the shape of the data " +
        "(JSON object keys or array length, CSV columns, the tables of a spreadsheet, Word or presentation document and their columns). " +
        "Supports DOCX, DOC, RTF, ODT, TXT, Markdown, HTML, XLSX, XLS, ODS, PPTX, PPT, ODP, MSG, EML templates and JSON, XML, CSV, TSV data. " +
        "Call it when the user asks whether a file is a template, what a data file contains, or what a template can be saved as. " +
        "Do NOT pre-check whether files exist — just pass the filename the user provided; a file that cannot be found returns the list of available files instead. " +
        "Returns JSON: { fileName, extension, size, family, isTemplate, tagCount, dataSourceNames[], canSaveTo[], dataKind, data }. " +
        "family is one of WordProcessing, Spreadsheet, Presentation, Email, or null for a data-only file; isTemplate is null when tags cannot be read (binary DOC/XLS/PPT/MSG). " +
        "On failure isError is set and the text starts with 'Document-info lookup failed for'.")]
    public static async Task<CallToolResult> GetDocumentInfo(
        IFileResolver resolver,
        ILicenseManager licenseManager,
        FileInput file)
    {
        licenseManager.SetLicense();
        using var resolved = await resolver.ResolveAsync(file);
        using var work = new TempWorkspace();

        try
        {
            var path = await work.SaveInputAsync(resolved);
            var ext = AssemblyFormats.Normalize(Path.GetExtension(resolved.FileName));
            var family = AssemblyFormats.FamilyOfExtension(ext);
            var dataKind = AssemblyFormats.DataKindOfExtension(ext);
            if (family is null && dataKind is null)
                throw new ToolInputException(
                    $"'{resolved.FileName}' is neither a template nor a data format GroupDocs.Assembly reads. Call get_supported_formats for the list.");

            TemplateAnalysis? analysis = null;
            if (family is not null)
            {
                var parts = TemplateText.Read(path);
                if (parts is not null) analysis = TemplateAnalyzer.Analyze(parts);
            }

            var payload = new
            {
                fileName = resolved.FileName,
                extension = ext,
                size = new FileInfo(path).Length,
                family,
                isTemplate = family is null ? false : analysis is null ? (bool?)null : analysis.Tags.Count > 0,
                tagCount = analysis?.Tags.Count,
                dataSourceNames = analysis?.DataSourceNames ?? [],
                canSaveTo = family is null ? [] : AssemblyFormats.SaveFormats(family),
                dataKind,
                data = dataKind is null ? null : DescribeData(dataKind, path)
            };
            return ToolError.Success(JsonSerializer.Serialize(payload, JsonOptions));
        }
        catch (Exception ex)
        {
            return ToolError.Failure("Document-info lookup", resolved.FileName, ex, hint: AssemblyTool.Hint(ex));
        }
    }

    private static object? DescribeData(string kind, string path)
    {
        switch (kind)
        {
            case AssemblyFormats.JsonData:
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(path),
                        new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                    var root = doc.RootElement;
                    return root.ValueKind switch
                    {
                        JsonValueKind.Object => new
                        {
                            root = "object",
                            keys = root.EnumerateObject().Select(p => new { name = p.Name, type = TypeOf(p.Value), length = p.Value.ValueKind == JsonValueKind.Array ? p.Value.GetArrayLength() : (int?)null }).ToList()
                        },
                        JsonValueKind.Array => new { root = "array", length = root.GetArrayLength(), keys = (object?)null } as object,
                        _ => new { root = TypeOf(root) }
                    };
                }
                catch (JsonException ex)
                {
                    return new { root = "unparsed", note = $"Not strict JSON ({ex.Message}). GroupDocs.Assembly also accepts relaxed JSON such as unquoted keys." };
                }
            }
            case AssemblyFormats.CsvData:
            {
                var lines = File.ReadLines(path).Where(l => l.Length > 0).Take(100_001).ToList();
                var tab = AssemblyFormats.Normalize(Path.GetExtension(path)) == "tsv";
                return new
                {
                    header = lines.Count > 0 ? lines[0].Split(tab ? '\t' : ',').Select(h => h.Trim().Trim('"')).ToList() : [],
                    rows = Math.Max(0, lines.Count - 1),
                    note = "Columns are split on the default delimiter; quoted values containing it are not accounted for."
                };
            }
            case AssemblyFormats.XmlData:
            {
                var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null };
                using var reader = System.Xml.XmlReader.Create(path, settings);
                reader.MoveToContent();
                return new { rootElement = reader.LocalName };
            }
            case AssemblyFormats.TableData:
            {
                var set = new DocumentTableSet(path, new HeaderRowHandler());
                var tables = set.Tables.Cast<DocumentTable>().ToList();
                return new
                {
                    tableCount = tables.Count,
                    tables = tables.Take(MaxTables).Select((t, i) => new
                    {
                        number = i + 1,
                        columns = t.Columns.Cast<DocumentTableColumn>().Select(c => c.Name).ToList()
                    }),
                    note = "Column names are read from each table's first row; pass hasHeaders=false to assemble_document when the first row is data. Use 'dataTable' to pick a table."
                };
            }
            default:
                return null;
        }
    }

    private static string TypeOf(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => "object",
        JsonValueKind.Array => "array",
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        _ => "null"
    };

    /// <summary>Loads every table with its first row as column names, as assemble_document does by default.</summary>
    private sealed class HeaderRowHandler : IDocumentTableLoadHandler
    {
        public void Handle(DocumentTableLoadArgs args) =>
            args.Options = new DocumentTableOptions { FirstRowContainsColumnNames = true };
    }
}
