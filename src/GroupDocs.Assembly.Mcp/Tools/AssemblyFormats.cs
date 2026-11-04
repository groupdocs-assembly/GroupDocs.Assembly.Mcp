namespace GroupDocs.Assembly.Mcp.Tools;

/// <summary>
/// Template families, the formats each can be saved to, and the data formats the server reads.
/// </summary>
/// <remarks>
/// The save tables were measured on GroupDocs.Assembly 26.7.0 by assembling one template of each family
/// into every extension the engine names (build-out plan, section 8.3). A target outside its family's
/// list fails in the engine with "The document cannot be saved to the given format." after the whole
/// template has been processed, so the server refuses it up front.
/// </remarks>
internal static class AssemblyFormats
{
    public const string WordProcessing = "WordProcessing";
    public const string Spreadsheet = "Spreadsheet";
    public const string Presentation = "Presentation";
    public const string Email = "Email";

    public static readonly string[] Families = [WordProcessing, Spreadsheet, Presentation, Email];

    /// <summary>
    /// Template extensions per family. Plain text, Markdown and HTML templates are loaded by the word
    /// processing engine, so they belong to that family and can be saved as DOCX or PDF.
    /// </summary>
    private static readonly Dictionary<string, string[]> TemplateExtensions = new()
    {
        [WordProcessing] = ["docx", "doc", "docm", "dotx", "dotm", "dot", "rtf", "odt", "ott", "txt", "md", "html", "htm", "mhtml", "xml", "wordml"],
        [Spreadsheet] = ["xlsx", "xls", "xlsm", "xlsb", "xltx", "xltm", "xlt", "ods"],
        [Presentation] = ["pptx", "ppt", "pptm", "pps", "ppsx", "ppsm", "potx", "potm", "pot", "odp", "otp"],
        [Email] = ["msg", "eml", "emlx"],
    };

    private static readonly string[] WordTargets =
        ["docx", "doc", "docm", "dotx", "dotm", "dot", "rtf", "odt", "ott", "txt", "md", "html", "mhtml", "xml", "wordml", "pdf", "xps", "epub", "ps", "pcl", "svg", "tiff"];

    private static readonly Dictionary<string, string[]> SaveTargets = new()
    {
        [WordProcessing] = WordTargets,
        [Spreadsheet] = ["xlsx", "xls", "xlsm", "xlsb", "xltx", "xltm", "ods", "html", "mhtml", "xml", "pdf", "xps", "pcl", "tiff"],
        [Presentation] = ["pptx", "ppt", "pptm", "pps", "ppsx", "potx", "pot", "odp", "otp", "html", "pdf", "xps", "tiff"],
        // An email template is a message body: it saves as a message or as any word-processing format.
        [Email] = ["eml", "emlx", "msg", .. WordTargets],
    };

    /// <summary>Extension → engine format. Only targets in <see cref="SaveTargets"/> are listed.</summary>
    private static readonly Dictionary<string, FileFormat> Formats = new()
    {
        ["docx"] = FileFormat.Docx, ["doc"] = FileFormat.Doc, ["docm"] = FileFormat.Docm, ["dotx"] = FileFormat.Dotx,
        ["dotm"] = FileFormat.Dotm, ["dot"] = FileFormat.Dot, ["rtf"] = FileFormat.Rtf, ["odt"] = FileFormat.Odt,
        ["ott"] = FileFormat.Ott, ["txt"] = FileFormat.Text, ["md"] = FileFormat.Markdown, ["html"] = FileFormat.Html,
        ["mhtml"] = FileFormat.Mhtml, ["xml"] = FileFormat.Xml, ["wordml"] = FileFormat.WordML, ["pdf"] = FileFormat.Pdf,
        ["xps"] = FileFormat.Xps, ["epub"] = FileFormat.Epub, ["ps"] = FileFormat.Ps, ["pcl"] = FileFormat.Pcl,
        ["svg"] = FileFormat.Svg, ["tiff"] = FileFormat.Tiff,
        ["xlsx"] = FileFormat.Xlsx, ["xls"] = FileFormat.Xls, ["xlsm"] = FileFormat.Xlsm, ["xlsb"] = FileFormat.Xlsb,
        ["xltx"] = FileFormat.Xltx, ["xltm"] = FileFormat.Xltm, ["ods"] = FileFormat.Ods,
        ["pptx"] = FileFormat.Pptx, ["ppt"] = FileFormat.Ppt, ["pptm"] = FileFormat.Pptm, ["pps"] = FileFormat.Pps,
        ["ppsx"] = FileFormat.Ppsx, ["potx"] = FileFormat.Potx, ["pot"] = FileFormat.Pot, ["odp"] = FileFormat.Odp,
        ["otp"] = FileFormat.Otp,
        ["eml"] = FileFormat.Eml, ["emlx"] = FileFormat.Emlx, ["msg"] = FileFormat.MsgUnicode,
    };

    /// <summary>Data file kinds the server reads.</summary>
    public const string JsonData = "json";
    public const string XmlData = "xml";
    public const string CsvData = "csv";
    public const string TableData = "table";

    /// <summary>
    /// Documents whose tables (or worksheets) can serve as data through the engine's DocumentTable.
    /// </summary>
    public static readonly string[] TableDataExtensions =
        ["xlsx", "xls", "xlsm", "xlsb", "ods", "docx", "doc", "odt", "rtf", "pptx", "ppt", "odp"];

    public static string Normalize(string? extension) =>
        (extension ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant() switch
        {
            "htm" => "html",
            "text" => "txt",
            "markdown" => "md",
            "tif" => "tiff",
            var e => e
        };

    /// <summary>The template family of a file extension, or null when the server does not know it.</summary>
    public static string? FamilyOfExtension(string? extension)
    {
        var ext = (extension ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        foreach (var (family, list) in TemplateExtensions)
            if (list.Contains(ext)) return family;
        return null;
    }

    public static IReadOnlyList<string> TemplateFormats(string family) => TemplateExtensions[family];

    public static IReadOnlyList<string> SaveFormats(string family) => SaveTargets[family];

    /// <summary>The data kind of a data file, or null when the extension is not a supported data format.</summary>
    public static string? DataKindOfExtension(string? extension) => Normalize(extension) switch
    {
        "json" => JsonData,
        "xml" => XmlData,
        "csv" or "tsv" => CsvData,
        var e when TableDataExtensions.Contains(e) => TableData,
        _ => null
    };

    /// <summary>
    /// Validates the output format against the template's family and returns the engine format.
    /// An unknown template extension skips the family check: the engine detects the format by content.
    /// </summary>
    public static FileFormat TargetFormat(string? family, string target)
    {
        if (!Formats.TryGetValue(target, out var format))
            throw new ToolInputException(
                $"'{target}' is not an output format GroupDocs.Assembly can write. Call get_supported_formats for the list.");
        if (family is not null && !SaveTargets[family].Contains(target))
            throw new ToolInputException(
                $"a {family} template cannot be saved as '{target}'. Targets: {string.Join(", ", SaveTargets[family])}.");
        return format;
    }
}
