using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GroupDocs.Assembly.Mcp.Tools;

[McpServerToolType]
public static class AssemblePerRecordTool
{
    public const int DefaultMaxDocuments = 100;
    public const int MaxDocumentsLimit = 1000;
    private const int ListedPaths = 50;

    [McpServerTool(Name = "assemble_per_record", Destructive = false), Description(
        "Generates one document per record — a mail merge: a letter per customer, a certificate per attendee, an invoice per order — " +
        "by filling the same template once for every element of a JSON array, and saves each result as its own file. " +
        "Use it when the user wants separate documents for each row or record; use assemble_document for one document that lists all records. " +
        "Templates: DOCX, DOC, RTF, ODT, TXT, Markdown, HTML, XLSX, PPTX, MSG, EML and more. " +
        "Data: 'dataJson' (inline) or 'data' (a .json file) holding an array of objects, or an object with the array under 'recordsPath' " +
        "(found automatically when the object has exactly one array). Each record is the template's data for one document: " +
        "its keys are the names the template uses (<<[Name]>>), or name the record with 'dataSourceName' (<<[customer.Name]>> → 'customer'; worked out from the template when omitted). " +
        "Files are named '<template>_<n>.<ext>', or after a record field with 'fileNameField'. At most 'maxDocuments' records (default 100, up to 1000). " +
        "Do NOT pre-check whether files exist — just pass the filename the user provided; a file that cannot be found returns the list of available files instead. " +
        "Returns 'Assembled N of M document(s) …' followed by one saved path per line, and the record number and reason for any record that failed. " +
        "On failure isError is set and the text starts with 'Per-record assembly failed for'.")]
    public static async Task<CallToolResult> AssemblePerRecord(
        IFileResolver resolver,
        IFileStorage storage,
        ILicenseManager licenseManager,
        [Description("The template document (DOCX, XLSX, PPTX, Markdown, HTML, TXT, MSG, EML, …) containing <<…>> tags")] FileInput template,
        [Description("The records as inline JSON: an array of objects, or an object holding the array. Use this OR 'data'")] string? dataJson = null,
        [Description("The records as a .json file: an array of objects, or an object holding the array. Use this OR 'dataJson'")] FileInput? data = null,
        [Description("Where the array is when the JSON is an object, as a dotted path (e.g. 'customers' or 'batch.items'). Default: its only array")] string? recordsPath = null,
        [Description("The name the template uses for one record (e.g. 'customer' in <<[customer.Name]>>). Default: worked out from the template")] string? dataSourceName = null,
        [Description("A record field whose value names each output file (e.g. 'InvoiceNumber' or 'customer.Name'). Default: a running number")] string? fileNameField = null,
        [Description("Output format extension (default: the template's own), e.g. 'pdf', 'docx', 'eml'")] string? format = null,
        [Description("Refuse to run when there are more records than this (default 100, at most 1000)")] int maxDocuments = DefaultMaxDocuments,
        [Description("Treat fields missing from a record as empty instead of failing that record (default false)")] bool allowMissingMembers = false,
        [Description("Remove paragraphs that become empty after tags are replaced (default false)")] bool removeEmptyParagraphs = false,
        [Description("Update fields (page numbers, tables of contents) and spreadsheet formulas in each result (default false)")] bool updateFields = false)
    {
        licenseManager.SetLicense();
        using var resolvedTemplate = await resolver.ResolveAsync(template);
        using var resolvedData = data is null || AssembleDocumentTool.IsEmpty(data) ? null : await resolver.ResolveAsync(data);
        using var work = new TempWorkspace();
        AssemblyJob? prepared = null;

        try
        {
            if (maxDocuments < 1 || maxDocuments > MaxDocumentsLimit)
                throw new ToolInputException($"'maxDocuments' must be between 1 and {MaxDocumentsLimit}.");

            var job = prepared = await AssemblyJob.PrepareAsync(work, resolvedTemplate, format, null, "");
            var (kind, dataPath, dataName) = await AssemblyJob.SaveDataAsync(work, resolvedData, dataJson);
            if (kind != AssemblyFormats.JsonData)
                throw new ToolInputException("per-record assembly reads JSON records only; pass the records as a .json file or as 'dataJson'.");

            var records = Records(await File.ReadAllTextAsync(dataPath), recordsPath);
            if (records.Count == 0)
                throw new ToolInputException("the records array is empty; there is nothing to generate.");
            if (records.Count > maxDocuments)
                throw new ToolInputException(
                    $"there are {records.Count} records, more than maxDocuments={maxDocuments}. Raise 'maxDocuments' (up to {MaxDocumentsLimit}) or pass fewer records.");

            var switches = new AssemblySwitches(allowMissingMembers, removeEmptyParagraphs, updateFields);
            var baseName = Path.GetFileNameWithoutExtension(resolvedTemplate.FileName);
            var width = records.Count.ToString().Length;
            var saved = new List<string>();
            var failures = new List<string>();
            string? usedName = null;

            for (var i = 0; i < records.Count; i++)
            {
                var number = i + 1;
                try
                {
                    if (records[i] is not JsonObject record)
                        throw new ToolInputException("the record is not a JSON object.");

                    var recordPath = work.PathFor($"data_record_{number}.json");
                    await File.WriteAllTextAsync(recordPath, record.ToJsonString());
                    var loaded = AssemblyData.Load(AssemblyFormats.JsonData, recordPath, dataSourceName, job.Analysis,
                        resolvedTemplate.FileName, new TableOptions());
                    usedName ??= loaded.Source.Name;

                    var stem = FileStem(record, fileNameField) ?? $"{baseName}_{number.ToString().PadLeft(width, '0')}";
                    var stored = await AssemblyTool.AssembleToStorageAsync(storage, work, job, $"{stem}.{job.Target}", switches,
                        loaded.Source, number.ToString());
                    saved.Add(stored.SavedPath);
                }
                catch (Exception ex)
                {
                    failures.Add($"record {number}: " + TempWorkspace.Scrub(Reason(ex, job.Analysis), resolvedTemplate.FileName));
                }
            }

            var text = new StringBuilder();
            text.Append($"Assembled {saved.Count} of {records.Count} document(s) from '{resolvedTemplate.FileName}' with {dataName}, one per record");
            if (usedName is not null) text.Append($" (each record as '{usedName}')");
            text.Append($", as {job.Target.ToUpperInvariant()}.");
            if (saved.Count > 0)
            {
                text.Append("\nSaved to:");
                foreach (var path in saved.Take(ListedPaths)) text.Append("\n").Append(path);
                if (saved.Count > ListedPaths) text.Append($"\n… and {saved.Count - ListedPaths} more in the same folder.");
            }
            if (failures.Count > 0)
            {
                text.Append($"\n{failures.Count} record(s) failed:");
                foreach (var f in failures.Take(ListedPaths)) text.Append("\n").Append(f);
                if (failures.Count > ListedPaths) text.Append($"\n… and {failures.Count - ListedPaths} more.");
            }

            if (saved.Count == 0)
                return ToolError.Failure($"Per-record assembly failed for '{resolvedTemplate.FileName}': no document was generated.\n" + text);
            return ToolError.Success(AssemblyTool.WithNotice(licenseManager, text.ToString()));
        }
        catch (Exception ex)
        {
            return ToolError.Failure("Per-record assembly", resolvedTemplate.FileName, ex, hint: AssemblyTool.Hint(ex, prepared?.Analysis));
        }
    }

    private static string Reason(Exception ex, TemplateAnalysis? template)
    {
        var message = ex is ToolInputException or ArgumentException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
        var hint = AssemblyTool.Hint(ex, template);
        return hint is null ? message : message + " " + hint;
    }

    /// <summary>The records array: the root array, the array at <paramref name="recordsPath"/>, or the object's only array.</summary>
    internal static IList<JsonNode?> Records(string json, string? recordsPath)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            throw new ToolInputException($"the records are not valid JSON: {ex.Message}");
        }

        if (!string.IsNullOrWhiteSpace(recordsPath))
        {
            var node = root;
            foreach (var key in recordsPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                node = node is JsonObject o && o.TryGetPropertyValue(key, out var child)
                    ? child
                    : throw new ToolInputException($"'recordsPath' '{recordsPath}' does not exist in the JSON (stopped at '{key}').");
            return node as JsonArray ?? throw new ToolInputException($"'recordsPath' '{recordsPath}' is not an array.");
        }

        switch (root)
        {
            case JsonArray array:
                return array;
            case JsonObject obj:
                var arrays = obj.Where(p => p.Value is JsonArray).ToList();
                return arrays.Count == 1
                    ? (JsonArray)arrays[0].Value!
                    : throw new ToolInputException(arrays.Count == 0
                        ? "the JSON object holds no array of records. Pass an array, or an object with one."
                        : $"the JSON object holds several arrays ({string.Join(", ", arrays.Select(a => a.Key))}); say which one with 'recordsPath'.");
            default:
                throw new ToolInputException("the records must be a JSON array, or an object holding one.");
        }
    }

    /// <summary>The file name stem from a record field; <paramref name="field"/> may be a dotted path ("customer.Name").</summary>
    internal static string? FileStem(JsonObject record, string? field)
    {
        if (string.IsNullOrWhiteSpace(field)) return null;
        JsonNode? value = record;
        foreach (var key in field.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            value = value is JsonObject o && o.TryGetPropertyValue(key, out var child)
                ? child
                : throw new ToolInputException($"the record has no '{field}' field to name its file.");
        if (value is not JsonValue v)
            throw new ToolInputException($"the record's '{field}' is {(value is null ? "null" : "an object or a list")}; name files after a text or number field.");
        var stem = AssemblyTool.SafeFileName(v.TryGetValue<string>(out var s) ? s : v.ToJsonString());
        return stem.Length == 0 ? throw new ToolInputException($"the record's '{field}' is empty and cannot name a file.") : stem;
    }
}
