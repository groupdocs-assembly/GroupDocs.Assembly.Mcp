using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GroupDocs.Assembly.Mcp.Tools;

[McpServerToolType]
public static class GetTemplateFieldsTool
{
    private const int MaxTags = 200;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "get_template_fields", ReadOnly = true, Idempotent = true), Description(
        "Lists what a template needs before generating a document from it: every <<…>> tag, the names it uses for its data, the fields it reads, " +
        "and a JSON skeleton of the data to fill in and pass to assemble_document as 'dataJson'. " +
        "Use it when the user asks what data a template expects, or before calling assemble_document with data you have to build. " +
        "Reads DOCX, DOTX, DOCM, RTF, ODT, TXT, Markdown, HTML, XLSX, XLTX, ODS, PPTX, POTX, ODP and EML templates; binary DOC, XLS, PPT and MSG templates " +
        "cannot be read without generating them (readable=false). Processes no data and does not run the engine. " +
        "Do NOT pre-check whether files exist — just pass the filename the user provided; a file that cannot be found returns the list of available files instead. " +
        "Returns JSON: { fileName, family, readable, tagCount, dataSourceNames[], fields[], dataSkeleton, tags: [{ tag, kind, part }], tagsTruncated, notes[] }. " +
        "fields use '[]' for lists ('customers[].Name'). The skeleton is inferred from the tags; check it against your data. " +
        "On failure isError is set and the text starts with 'Template inspection failed for'.")]
    public static async Task<CallToolResult> GetTemplateFields(
        IFileResolver resolver,
        ILicenseManager licenseManager,
        [Description("The template document to inspect")] FileInput template)
    {
        licenseManager.SetLicense();
        using var resolved = await resolver.ResolveAsync(template);
        using var work = new TempWorkspace();

        try
        {
            var path = await work.SaveInputAsync(resolved);
            var family = AssemblyFormats.FamilyOfExtension(Path.GetExtension(resolved.FileName));
            if (family is null)
                throw new ToolInputException(
                    $"'{resolved.FileName}' is not a template format GroupDocs.Assembly reads. Call get_supported_formats for the list.");

            var parts = TemplateText.Read(path);
            if (parts is null)
            {
                return ToolError.Success(JsonSerializer.Serialize(new
                {
                    fileName = resolved.FileName,
                    family,
                    readable = false,
                    tagCount = (int?)null,
                    dataSourceNames = Array.Empty<string>(),
                    fields = Array.Empty<string>(),
                    dataSkeleton = new JsonObject(),
                    tags = Array.Empty<object>(),
                    tagsTruncated = false,
                    notes = new[]
                    {
                        $"Tags cannot be read from a .{AssemblyFormats.Normalize(Path.GetExtension(resolved.FileName))} template without generating it. " +
                        "Save the template as DOCX, XLSX, PPTX or EML to inspect it, or generate it with assemble_document and inlineErrors=true to see which fields fail."
                    }
                }, JsonOptions));
            }

            var analysis = TemplateAnalyzer.Analyze(parts);
            var notes = analysis.Notes.ToList();
            if (analysis.Tags.Count == 0)
                notes.Insert(0, "No <<…>> tags were found: this document is not a GroupDocs.Assembly template, or its tags are in a part the server does not read (text boxes in charts, embedded objects).");

            var payload = new
            {
                fileName = resolved.FileName,
                family,
                readable = true,
                tagCount = analysis.Tags.Count,
                dataSourceNames = analysis.DataSourceNames,
                fields = analysis.Fields,
                dataSkeleton = analysis.Skeleton,
                tags = analysis.Tags.Take(MaxTags).Select(t => new { tag = Shorten(t.Text), kind = t.Kind, part = t.Part }),
                tagsTruncated = analysis.Tags.Count > MaxTags,
                notes
            };
            return ToolError.Success(JsonSerializer.Serialize(payload, JsonOptions));
        }
        catch (Exception ex)
        {
            return ToolError.Failure("Template inspection", resolved.FileName, ex);
        }
    }

    private static string Shorten(string tag)
    {
        var flat = System.Text.RegularExpressions.Regex.Replace(tag, @"\s+", " ");
        return flat.Length <= 200 ? flat : flat[..197] + "...";
    }
}
