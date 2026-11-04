using System.Text.RegularExpressions;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;

namespace GroupDocs.Assembly.Mcp.Tools;

/// <summary>Switches of one assembly run, mapped onto <see cref="DocumentAssemblyOptions"/>.</summary>
internal sealed record AssemblySwitches(
    bool AllowMissingMembers = false,
    bool RemoveEmptyParagraphs = false,
    bool UpdateFields = false,
    bool InlineErrors = false)
{
    public DocumentAssemblyOptions ToOptions()
    {
        // Spreadsheet data types: numbers and dates land in cells as numbers and dates, not text.
        // The engine applies it to spreadsheet templates only.
        var options = DocumentAssemblyOptions.UseSpreadsheetDataTypes;
        if (AllowMissingMembers) options |= DocumentAssemblyOptions.AllowMissingMembers;
        if (RemoveEmptyParagraphs) options |= DocumentAssemblyOptions.RemoveEmptyParagraphs;
        if (UpdateFields) options |= DocumentAssemblyOptions.UpdateFieldsAndFormulas;
        if (InlineErrors) options |= DocumentAssemblyOptions.InlineErrorMessages;
        return options;
    }
}

/// <summary>Helpers shared by the tool classes.</summary>
internal static class AssemblyTool
{
    /// <summary>
    /// One line, prefixed to the text of every generating tool when unlicensed. Spelled "Evaluation mode:"
    /// (not "[Evaluation mode]") because some clients locate structured output by its first '['.
    /// Measured on GroupDocs.Assembly 26.7.0; see the build-out plan, section 8.3.
    /// </summary>
    public const string EvaluationNotice =
        "Evaluation mode: generated documents start with an 'Evaluation Only' line, saved workbooks gain an extra worksheet, " +
        "and e-mail messages keep only their plain-text body (the HTML body is replaced). " +
        "Set GROUPDOCS_LICENSE_PATH, or the metered keys, to remove these limits.";

    public static string WithNotice(ILicenseManager license, string text) =>
        license.IsLicensed ? text : EvaluationNotice + "\n\n" + text;

    /// <summary>Runs the engine. Returns false when <c>InlineErrors</c> is on and the template had errors.</summary>
    public static bool Assemble(string templatePath, string outputPath, FileFormat format, AssemblySwitches switches,
        params DataSourceInfo[] sources)
    {
        var assembler = new DocumentAssembler { Options = switches.ToOptions() };
        return assembler.AssembleDocument(templatePath, outputPath, new LoadSaveOptions(format), sources);
    }

    private static readonly Regex MissingMember = new(@"Can not get the value of member '(?<m>[^']*)' on type '(?<t>[^']*)'", RegexOptions.Compiled);

    /// <summary>
    /// A plain-words explanation for engine failures whose own message misleads or stops short. Returns null
    /// when the engine message is already clear.
    /// </summary>
    public static string? Hint(Exception ex, TemplateAnalysis? template = null)
    {
        var message = Flatten(ex);
        var missing = MissingMember.Match(message);
        if (missing.Success)
        {
            var member = missing.Groups["m"].Value;
            if (template?.DataSourceNames.Contains(member) == true)
                return $"The template uses the name '{member}' for data it was not given. Pass the data under that name: 'dataSourceName'='{member}', " +
                       $"or a JSON object with a '{member}' key. get_template_fields lists the names a template expects.";
            return $"The template reads '{member}', which the data does not have. Check the spelling (names are case-sensitive) with get_template_fields " +
                   "and add it to the data. A list that is empty in the data has no fields at all, so reading one fails too; " +
                   "pass allowMissingMembers=true to output nothing for missing members and empty lists.";
        }
        if (message.Contains("Unexpected end of template", StringComparison.Ordinal))
            return "A block tag is not closed: every <<foreach>> needs <</foreach>> and every <<if>> needs <</if>>.";
        if (message.Contains("The type initializer for 'Gdip'", StringComparison.Ordinal) || message.Contains("System.Drawing.Common is not supported", StringComparison.Ordinal))
            return "Presentation templates need the native GDI+ library on Linux and macOS: install libgdiplus (apt-get install libgdiplus, or brew install mono-libgdiplus), or run the Docker image, which includes it.";
        if (message.Contains("cannot be saved to the given format", StringComparison.Ordinal))
            return "Call get_supported_formats for the formats this template can be saved as.";
        return ex switch
        {
            UnauthorizedAccessException => NotWritableHint,
            _ => null
        };
    }

    private static string Flatten(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException) parts.Add(e.Message);
        return string.Join(" | ", parts);
    }

    /// <summary>
    /// The common cause of a denied write is Docker on Linux: the image runs as a non-root user and a
    /// bind mount keeps its host owner, so a folder the user owns is read-only inside the container.
    /// </summary>
    public const string NotWritableHint =
        "The output folder is not writable by the server process. In Docker on Linux the container runs as a " +
        "non-root user, so a host folder you own is read-only to it: add --user \"$(id -u):$(id -g)\" to docker run. " +
        "Otherwise point GROUPDOCS_MCP_OUTPUT_PATH at a writable folder.";

    /// <summary>Output file name in storage: base name of the template plus a suffix and the target extension.</summary>
    public static string OutputName(string templateName, string suffix, string extension) =>
        $"{Path.GetFileNameWithoutExtension(templateName)}{suffix}.{AssemblyFormats.Normalize(extension)}";

    /// <summary>Validates and normalizes a caller-chosen output name, forcing the target extension.</summary>
    public static string? CallerOutputName(string? outputName, string extension)
    {
        if (string.IsNullOrWhiteSpace(outputName)) return null;
        var name = Path.GetFileName(outputName.Trim());
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ToolInputException($"'outputName' '{outputName}' is not a valid file name.");
        return $"{Path.GetFileNameWithoutExtension(name)}.{AssemblyFormats.Normalize(extension)}";
    }

    /// <summary>A file name made from a data value: invalid characters replaced, length capped.</summary>
    public static string SafeFileName(string value, int maxLength = 80)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':']).ToHashSet();
        var chars = value.Trim().Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray();
        var name = new string(chars).Trim('.', ' ');
        return name.Length > maxLength ? name[..maxLength] : name;
    }

    /// <summary>The stored result of one assembly run.</summary>
    /// <param name="SavedPath">Where storage wrote the document (absolute).</param>
    /// <param name="Clean">False when inline error messages were written into the output.</param>
    /// <param name="ResourceFolder">The folder of images an HTML output links to, or null.</param>
    public sealed record StoredOutput(string SavedPath, bool Clean, string? ResourceFolder);

    /// <summary>
    /// Assembles into the workspace and stores the result under <paramref name="name"/>. Storage de-duplicates
    /// an existing name with " (1)", so an output never replaces a file the caller already has.
    /// </summary>
    /// <remarks>
    /// An HTML output keeps its images in "&lt;name&gt;_files/" beside it and links to them by that name, so the
    /// pair must share one base name. Storage has no delete, so the name is reserved only after assembly has
    /// succeeded; when storage hands back a de-duplicated name the document is assembled again under it, so the
    /// engine writes links that match.
    /// </remarks>
    public static async Task<StoredOutput> AssembleToStorageAsync(IFileStorage storage, TempWorkspace work, AssemblyJob job,
        string name, AssemblySwitches switches, DataSourceInfo source, string slot)
    {
        var dir = Path.Combine(work.Root, "out_" + slot);
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, name);
        var clean = Assemble(job.TemplatePath, temp, job.Format, switches, source);

        if (Directory.GetDirectories(dir).Length == 0)
            return new StoredOutput(await storage.WriteFileAsync(name, await File.ReadAllBytesAsync(temp), rewrite: false), clean, null);

        var stored = Path.GetFileName(await storage.WriteFileAsync(name, [], rewrite: false));
        if (stored != name)
        {
            Directory.Delete(dir, recursive: true);
            Directory.CreateDirectory(dir);
            temp = Path.Combine(dir, stored);
            clean = Assemble(job.TemplatePath, temp, job.Format, switches, source);
        }

        string? folder = null;
        foreach (var resourceDir in Directory.GetDirectories(dir))
        {
            folder ??= Path.GetFileName(resourceDir) + "/";
            foreach (var file in Directory.EnumerateFiles(resourceDir, "*", System.IO.SearchOption.AllDirectories))
                await storage.WriteFileAsync(Path.GetRelativePath(dir, file).Replace('\\', '/'), await File.ReadAllBytesAsync(file), rewrite: true);
        }
        var saved = await storage.WriteFileAsync(stored, await File.ReadAllBytesAsync(temp), rewrite: true);
        return new StoredOutput(saved, clean, folder);
    }
}
