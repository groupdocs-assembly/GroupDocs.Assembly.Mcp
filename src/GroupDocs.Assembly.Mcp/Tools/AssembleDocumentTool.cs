using System.ComponentModel;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GroupDocs.Assembly.Mcp.Tools;

[McpServerToolType]
public static class AssembleDocumentTool
{
    [McpServerTool(Name = "assemble_document", Destructive = false), Description(
        "Generates a document from a template and data: fills the template's <<[field]>>, <<foreach>>, <<if>>, table, chart, image and barcode tags " +
        "with values and saves the result — a contract, invoice, letter, report or e-mail built from a Word, Excel, PowerPoint, Markdown, HTML or e-mail template. " +
        "Use it whenever the user asks to generate, fill in, populate or produce a document from a template. " +
        "Templates: DOCX, DOC, RTF, ODT, TXT, Markdown, HTML, XLSX, XLS, ODS, PPTX, PPT, ODP, MSG, EML and more. " +
        "Data: pass 'dataJson' (a JSON object or array, inline) or 'data' (a .json, .xml, .csv/.tsv file, or a spreadsheet, Word or presentation document whose table is the data). " +
        "A JSON object's keys are the names the template uses ({\"customer\":{…},\"items\":[…]} fills <<[customer.Name]>> and <<foreach [i in items]>>); " +
        "an array, XML, CSV or table is given the name the template uses for it automatically, or pass 'dataSourceName'. " +
        "Call get_template_fields first to see which names and fields a template expects and a JSON skeleton to fill. " +
        "Output: the template's own format by default; set 'format' (e.g. pdf, docx, html, md) to save it as another format of the same family, or as PDF. " +
        "Do NOT pre-check whether files exist — just pass the filename the user provided; a file that cannot be found returns the list of available files instead. " +
        "Returns 'Assembled …' with the saved path ('Saved to: …') of '<template>_assembled.<ext>'. " +
        "On failure isError is set and the text starts with 'Document assembly failed for'; a field the data lacks is named in the message.")]
    public static async Task<CallToolResult> AssembleDocument(
        IFileResolver resolver,
        IFileStorage storage,
        ILicenseManager licenseManager,
        OutputHelper output,
        [Description("The template document (DOCX, XLSX, PPTX, Markdown, HTML, TXT, MSG, EML, …) containing <<…>> tags")] FileInput template,
        [Description("The data as inline JSON: an object whose keys are the names used in the template, or an array. Use this OR 'data'")] string? dataJson = null,
        [Description("The data as a file: .json, .xml, .csv or .tsv, or a spreadsheet/Word/presentation document whose table holds the data. Use this OR 'dataJson'")] FileInput? data = null,
        [Description("The name the template uses for the data (e.g. 'customers' in <<foreach [c in customers]>>). Default: worked out from the template")] string? dataSourceName = null,
        [Description("Output format extension (default: the template's own), e.g. 'pdf', 'docx', 'html', 'md', 'xlsx', 'pptx', 'eml'")] string? format = null,
        [Description("Output file name (the extension follows the output format). Default: '<template>_assembled.<ext>'")] string? outputName = null,
        [Description("Treat fields missing from the data as empty instead of failing (default false)")] bool allowMissingMembers = false,
        [Description("Remove paragraphs that become empty after tags are replaced (default false)")] bool removeEmptyParagraphs = false,
        [Description("Update fields (page numbers, tables of contents) and spreadsheet formulas in the result (default false)")] bool updateFields = false,
        [Description("Write template syntax errors into the output next to the faulty tag instead of failing — for debugging a template (default false)")] bool inlineErrors = false,
        [Description("Document data only: which table (worksheet for spreadsheets) holds the data, 1-based (default 1)")] int? dataTable = null,
        [Description("CSV and document-table data: the first row holds column names (default true)")] bool hasHeaders = true,
        [Description("CSV data: the column delimiter, one character or 'tab' (default ',' — tab for .tsv)")] string? csvDelimiter = null)
    {
        licenseManager.SetLicense();
        using var resolvedTemplate = await resolver.ResolveAsync(template);
        using var resolvedData = data is null || IsEmpty(data) ? null : await resolver.ResolveAsync(data);
        using var work = new TempWorkspace();
        AssemblyJob? job = null;

        try
        {
            job = await AssemblyJob.PrepareAsync(work, resolvedTemplate, format, outputName, "_assembled");
            var (kind, dataPath, dataName) = await AssemblyJob.SaveDataAsync(work, resolvedData, dataJson);
            var loaded = AssemblyData.Load(kind, dataPath, dataSourceName, job.Analysis, resolvedTemplate.FileName,
                new TableOptions(hasHeaders, csvDelimiter, dataTable));

            var switches = new AssemblySwitches(allowMissingMembers, removeEmptyParagraphs, updateFields, inlineErrors);
            var stored = await AssemblyTool.AssembleToStorageAsync(storage, work, job, job.OutputName, switches, loaded.Source, "main");

            var description = $"Assembled '{resolvedTemplate.FileName}' with {dataName} ({loaded.Description}) as {job.Target.ToUpperInvariant()}";
            if (stored.ResourceFolder is not null)
                description += $", images in '{stored.ResourceFolder}' beside it";
            if (!stored.Clean)
                description += ". The template has errors: each one is written into the output next to its tag, starting with 'Error!'";
            var message = await output.BuildFileOutputAsync(stored.SavedPath, description);
            return ToolError.Success(AssemblyTool.WithNotice(licenseManager, message));
        }
        catch (Exception ex)
        {
            return ToolError.Failure("Document assembly", resolvedTemplate.FileName, ex, hint: AssemblyTool.Hint(ex, job?.Analysis));
        }
    }

    /// <summary>An optional FileInput the client sent as an empty object.</summary>
    internal static bool IsEmpty(FileInput input) =>
        string.IsNullOrEmpty(input.FilePath) && string.IsNullOrEmpty(input.FileName) && string.IsNullOrEmpty(input.FileContent);
}
