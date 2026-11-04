using GroupDocs.Mcp.Core;

namespace GroupDocs.Assembly.Mcp.Tools;

/// <summary>A template copied into the workspace, with its family, analysis and output target settled.</summary>
internal sealed record AssemblyJob(
    string TemplatePath,
    string? Family,
    TemplateAnalysis? Analysis,
    string Target,
    FileFormat Format,
    string OutputName)
{
    /// <summary>
    /// Copies the template into the workspace (its extension drives format detection), reads its tags when
    /// the format allows, and validates the output format against the template's family before any engine work.
    /// </summary>
    public static async Task<AssemblyJob> PrepareAsync(TempWorkspace work, ResolvedFile template, string? format,
        string? outputName, string suffix)
    {
        var path = await work.SaveInputAsync(template);
        var ext = AssemblyFormats.Normalize(Path.GetExtension(template.FileName));
        var family = AssemblyFormats.FamilyOfExtension(ext);
        if (family is null && string.IsNullOrWhiteSpace(format))
            throw new ToolInputException(
                $"'{template.FileName}' is not a template format GroupDocs.Assembly reads by its extension. " +
                "Use a DOCX, XLSX, PPTX, Markdown, HTML, TXT, MSG or EML template, or pass 'format' to say what to save it as.");

        var target = string.IsNullOrWhiteSpace(format) ? ext : AssemblyFormats.Normalize(format);
        var engineFormat = AssemblyFormats.TargetFormat(family, target);

        var parts = TemplateText.Read(path);
        var analysis = parts is null ? null : TemplateAnalyzer.Analyze(parts);

        var name = AssemblyTool.CallerOutputName(outputName, target) ?? AssemblyTool.OutputName(template.FileName, suffix, target);
        return new AssemblyJob(path, family, analysis, target, engineFormat, name);
    }

    /// <summary>
    /// Writes the data into the workspace and returns its kind, path, and how to name it in the response.
    /// Exactly one of <paramref name="data"/> and <paramref name="dataJson"/> must be given.
    /// </summary>
    public static async Task<(string Kind, string Path, string Name)> SaveDataAsync(TempWorkspace work, ResolvedFile? data, string? dataJson)
    {
        var hasJson = !string.IsNullOrWhiteSpace(dataJson);
        if (data is not null && hasJson)
            throw new ToolInputException("pass the data once: either 'data' (a file) or 'dataJson' (inline JSON), not both.");
        if (data is null && !hasJson)
            throw new ToolInputException(
                "no data was given. Pass 'dataJson' (inline JSON) or 'data' (a .json, .xml, .csv file or a document with a table); " +
                "pass dataJson='{}' when the template needs no data.");

        if (hasJson)
        {
            var path = work.PathFor("data_inline.json");
            await File.WriteAllTextAsync(path, dataJson);
            return (AssemblyFormats.JsonData, path, "inline JSON");
        }

        var kind = AssemblyFormats.DataKindOfExtension(Path.GetExtension(data!.FileName))
                   ?? throw new ToolInputException(
                       $"'{data.FileName}' is not a data format GroupDocs.Assembly reads. Use .json, .xml, .csv or .tsv, " +
                       "or a spreadsheet, Word or presentation document with a table.");
        return (kind, await work.SaveInputAsync(data, "data_" + data.FileName), $"'{data.FileName}'");
    }
}
