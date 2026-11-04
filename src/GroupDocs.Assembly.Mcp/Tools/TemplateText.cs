using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace GroupDocs.Assembly.Mcp.Tools;

/// <summary>One part of a template (document body, a header, a worksheet, a slide, a chart) as text.</summary>
/// <param name="Name">Where the text comes from, e.g. "body", "header1", "worksheet 2", "slide 3".</param>
/// <param name="Units">Paragraphs or cells in document order. A tag may span units (a multi-line foreach).</param>
internal sealed record TemplatePart(string Name, IReadOnlyList<string> Units);

/// <summary>
/// Reads the visible text of a template without the engine, so its tags can be listed before any data
/// exists. Office Open XML, OpenDocument, plain text, Markdown, HTML, RTF and EML are read; binary formats
/// (DOC, XLS, PPT, MSG, XLSB) are not, and return null.
/// </summary>
/// <remarks>
/// Order matters: a <c>&lt;&lt;foreach&gt;&gt;</c> opens a scope that later tags live in, so text is read in
/// document order — body paragraphs as they appear, worksheet cells row by row (not the shared-strings
/// table, which is in first-use order), slides by number. Word splits a typed tag across runs freely, so
/// runs are joined per paragraph before tags are matched.
/// </remarks>
internal static class TemplateText
{
    private const long MaxPartBytes = 64L * 1024 * 1024;

    public static IReadOnlyList<TemplatePart>? Read(string path)
    {
        var ext = AssemblyFormats.Normalize(Path.GetExtension(path));
        return ext switch
        {
            "docx" or "docm" or "dotx" or "dotm" => ReadZip(path, WordParts),
            "xlsx" or "xlsm" or "xltx" or "xltm" => ReadZip(path, SpreadsheetParts),
            "pptx" or "pptm" or "potx" or "potm" or "ppsx" or "ppsm" => ReadZip(path, PresentationParts),
            "odt" or "ott" or "ods" or "odp" or "otp" => ReadZip(path, OpenDocumentParts),
            "txt" or "md" => [new TemplatePart("body", [ReadAllText(path)])],
            "html" or "mhtml" => [new TemplatePart("body", [HtmlToText(ReadAllText(path))])],
            "eml" or "emlx" => [new TemplatePart("message", [HtmlToText(DecodeQuotedPrintable(ReadAllText(path)))])],
            "rtf" => [new TemplatePart("body", [RtfToText(ReadAllText(path))])],
            _ => null
        };
    }

    private static string ReadAllText(string path) => File.ReadAllText(path);

    private static IReadOnlyList<TemplatePart>? ReadZip(string path, Func<ZipArchive, IReadOnlyList<TemplatePart>> reader)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return reader(zip);
        }
        catch (InvalidDataException)
        {
            return null; // not a zip: a mislabelled binary file
        }
    }

    // ---- Word -------------------------------------------------------------------------------

    private static readonly Regex WordPart = new(@"^word/(document|header\d*|footer\d*|footnotes|endnotes)\.xml$", RegexOptions.IgnoreCase);
    private static readonly Regex ChartPart = new(@"^(word|xl|ppt)/charts/chart(\d+)\.xml$", RegexOptions.IgnoreCase);

    private static IReadOnlyList<TemplatePart> WordParts(ZipArchive zip)
    {
        var parts = new List<TemplatePart>();
        var entries = zip.Entries.Where(e => WordPart.IsMatch(e.FullName))
            .OrderBy(e => e.FullName.EndsWith("document.xml", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(e => e.FullName, StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            var name = Path.GetFileNameWithoutExtension(e.Name);
            parts.Add(new TemplatePart(name == "document" ? "body" : name, Paragraphs(e, "p", "t", "tab", "br")));
        }
        parts.AddRange(Charts(zip));
        return parts;
    }

    // ---- Spreadsheet ------------------------------------------------------------------------

    private static readonly Regex SheetPart = new(@"^xl/worksheets/sheet(\d+)\.xml$", RegexOptions.IgnoreCase);

    private static IReadOnlyList<TemplatePart> SpreadsheetParts(ZipArchive zip)
    {
        var shared = new List<string>();
        var sst = zip.GetEntry("xl/sharedStrings.xml");
        if (sst is not null) shared.AddRange(Paragraphs(sst, "si", "t", null, null));

        var parts = new List<TemplatePart>();
        foreach (var (entry, number) in Numbered(zip, SheetPart))
            parts.Add(new TemplatePart($"worksheet {number}", Cells(entry, shared)));
        parts.AddRange(Charts(zip));
        return parts;
    }

    /// <summary>Cell texts of one worksheet in row order: shared and inline strings.</summary>
    private static List<string> Cells(ZipArchiveEntry entry, List<string> shared)
    {
        var units = new List<string>();
        using var reader = XmlReader.Create(Open(entry), Settings);
        string? type = null;
        var inValue = false;
        var inline = new StringBuilder();
        var inInline = false;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "c": type = reader.GetAttribute("t"); break;
                    case "v": inValue = !reader.IsEmptyElement; break;
                    case "is": inInline = true; inline.Clear(); break;
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (reader.LocalName == "v") inValue = false;
                else if (reader.LocalName == "is") { inInline = false; units.Add(inline.ToString()); }
            }
            else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace)
            {
                if (inInline) inline.Append(reader.Value);
                else if (inValue && type == "s" && int.TryParse(reader.Value, out var i) && i >= 0 && i < shared.Count)
                    units.Add(shared[i]);
                else if (inValue && type == "str") units.Add(reader.Value);
            }
        }
        return units;
    }

    // ---- Presentation -----------------------------------------------------------------------

    private static readonly Regex SlidePart = new(@"^ppt/slides/slide(\d+)\.xml$", RegexOptions.IgnoreCase);

    private static IReadOnlyList<TemplatePart> PresentationParts(ZipArchive zip)
    {
        var parts = Numbered(zip, SlidePart)
            .Select(s => new TemplatePart($"slide {s.Number}", Paragraphs(s.Entry, "p", "t", null, "br")))
            .ToList();
        parts.AddRange(Charts(zip));
        return parts;
    }

    // ---- OpenDocument -----------------------------------------------------------------------

    private static IReadOnlyList<TemplatePart> OpenDocumentParts(ZipArchive zip)
    {
        var content = zip.GetEntry("content.xml");
        if (content is null) return [];
        var units = new List<string>();
        using var reader = XmlReader.Create(Open(content), Settings);
        var stack = new Stack<StringBuilder>();
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.LocalName is "p" or "h")
                {
                    if (reader.IsEmptyElement) units.Add(string.Empty);
                    else stack.Push(new StringBuilder());
                }
                else if (stack.Count > 0 && reader.LocalName is "s" or "tab" or "line-break")
                {
                    var n = reader.LocalName == "s" && int.TryParse(reader.GetAttribute("text:c") ?? reader.GetAttribute("c"), out var c) ? c : 1;
                    stack.Peek().Append(' ', n);
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName is "p" or "h" && stack.Count > 0)
            {
                units.Add(stack.Pop().ToString());
            }
            else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace && stack.Count > 0)
            {
                stack.Peek().Append(reader.Value);
            }
        }
        return [new TemplatePart("body", units)];
    }

    // ---- shared helpers ---------------------------------------------------------------------

    private static IEnumerable<TemplatePart> Charts(ZipArchive zip) =>
        Numbered(zip, ChartPart).Select(c => new TemplatePart($"chart {c.Number}", Paragraphs(c.Entry, "p", "t", null, null)
            .Concat(Paragraphs(c.Entry, "pt", "v", null, null)).ToList()));

    private static IEnumerable<(ZipArchiveEntry Entry, int Number)> Numbered(ZipArchive zip, Regex pattern) =>
        zip.Entries
            .Select(e => (Entry: e, Match: pattern.Match(e.FullName)))
            .Where(x => x.Match.Success)
            .Select(x => (x.Entry, Number: int.Parse(x.Match.Groups[x.Match.Groups.Count - 1].Value)))
            .OrderBy(x => x.Number);

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    private static Stream Open(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxPartBytes)
            throw new ToolInputException($"the template part '{entry.FullName}' is too large to inspect ({entry.Length / (1024 * 1024)} MB).");
        return entry.Open();
    }

    /// <summary>
    /// Text of every <paramref name="unit"/> element (paragraph, shared string), joining the text of its
    /// <paramref name="text"/> children. Nested units (a text box inside a paragraph) are emitted on their own.
    /// </summary>
    private static List<string> Paragraphs(ZipArchiveEntry entry, string unit, string text, string? tab, string? br)
    {
        var units = new List<string>();
        using var reader = XmlReader.Create(Open(entry), Settings);
        var stack = new Stack<StringBuilder>();
        var inText = 0;
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element when reader.LocalName == unit:
                    if (reader.IsEmptyElement) units.Add(string.Empty);
                    else stack.Push(new StringBuilder());
                    break;
                case XmlNodeType.Element when reader.LocalName == text && !reader.IsEmptyElement:
                    inText++;
                    break;
                case XmlNodeType.Element when stack.Count > 0 && (reader.LocalName == tab || reader.LocalName == br):
                    stack.Peek().Append(' ');
                    break;
                case XmlNodeType.EndElement when reader.LocalName == text && inText > 0:
                    inText--;
                    break;
                case XmlNodeType.EndElement when reader.LocalName == unit && stack.Count > 0:
                    units.Add(stack.Pop().ToString());
                    break;
                case XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace when inText > 0 && stack.Count > 0:
                    stack.Peek().Append(reader.Value);
                    break;
            }
        }
        return units;
    }

    private static readonly Regex RawTag = new(@"<<[/\[A-Za-z][^\r\n]*?>>", RegexOptions.Compiled);
    private static readonly Regex Markup = new(@"<!--.*?-->|<(script|style)\b.*?</\1\s*>|<[^>]*>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// HTML templates normally carry tags entity-encoded ("&amp;lt;&amp;lt;[x]&amp;gt;&amp;gt;"), but a tag typed
    /// raw into the source is valid too; protect raw tags from the markup stripper, then decode.
    /// </summary>
    internal static string HtmlToText(string html)
    {
        var raw = new List<string>();
        var protectedHtml = RawTag.Replace(html, m => { raw.Add(m.Value); return $"\u0001{raw.Count - 1}\u0002"; });
        var text = WebUtility.HtmlDecode(Markup.Replace(protectedHtml, " "));
        return Regex.Replace(text, "\u0001(\\d+)\u0002", m => raw[int.Parse(m.Groups[1].Value)]);
    }

    internal static string DecodeQuotedPrintable(string text)
    {
        if (!text.Contains("quoted-printable", StringComparison.OrdinalIgnoreCase)) return text;
        var joined = Regex.Replace(text, "=\r?\n", string.Empty);
        return Regex.Replace(joined, "=([0-9A-Fa-f]{2})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
    }

    /// <summary>Plain text of an RTF document: control words, groups and hex escapes removed.</summary>
    internal static string RtfToText(string rtf)
    {
        var text = Regex.Replace(rtf, @"\\'([0-9a-fA-F]{2})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
        text = Regex.Replace(text, @"\\(par|line|tab)\b ?", " ");
        text = Regex.Replace(text, @"\{\\\*[^{}]*\}", string.Empty);
        text = Regex.Replace(text, @"\\[a-zA-Z]+-?\d* ?", string.Empty);
        return text.Replace("{", string.Empty).Replace("}", string.Empty).Replace("\\\\", "\\");
    }
}
