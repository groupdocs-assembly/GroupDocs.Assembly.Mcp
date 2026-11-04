using System.IO.Compression;
using System.Text;
using GroupDocs.Assembly.Mcp.Tools;
using Xunit;

namespace GroupDocs.Assembly.Mcp.Tests;

public sealed class TemplateTextTests : IDisposable
{
    private readonly TempWorkspace _work = new();

    public void Dispose() => _work.Dispose();

    private string Zip(string name, params (string Entry, string Xml)[] entries)
    {
        var path = _work.PathFor(name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, xml) in entries)
        {
            using var w = new StreamWriter(zip.CreateEntry(entry).Open(), new UTF8Encoding(false));
            w.Write(xml);
        }
        return path;
    }

    private string File(string name, string content)
    {
        var path = _work.PathFor(name);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    private const string W = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";

    [Fact]
    public void Docx_JoinsRunsPerParagraph_AndReadsHeaders()
    {
        // Word splits a typed tag into runs at will: "<<[cust" + "omer.Name]>>".
        var path = Zip("t.docx",
            ("word/document.xml", $"<w:document {W}><w:body><w:p><w:r><w:t>Dear &lt;&lt;[cust</w:t></w:r><w:r><w:t>omer.Name]&gt;&gt;</w:t></w:r></w:p>" +
                                  "<w:p><w:r><w:t xml:space=\"preserve\">a</w:t><w:tab/><w:t>b</w:t></w:r></w:p></w:body></w:document>"),
            ("word/header1.xml", $"<w:hdr {W}><w:p><w:r><w:t>&lt;&lt;[company]&gt;&gt;</w:t></w:r></w:p></w:hdr>"));

        var parts = TemplateText.Read(path)!;

        Assert.Equal("body", parts[0].Name);
        Assert.Equal(["Dear <<[customer.Name]>>", "a b"], parts[0].Units);
        Assert.Equal("header1", parts[1].Name);
        Assert.Equal(["<<[company]>>"], parts[1].Units);
    }

    [Fact]
    public void Docx_DeletedTextIsIgnored()
    {
        var path = Zip("t.docx", ("word/document.xml",
            $"<w:document {W}><w:body><w:p><w:del><w:r><w:delText>&lt;&lt;[old]&gt;&gt;</w:delText></w:r></w:del><w:r><w:t>&lt;&lt;[new]&gt;&gt;</w:t></w:r></w:p></w:body></w:document>"));
        Assert.Equal(["<<[new]>>"], TemplateText.Read(path)![0].Units);
    }

    [Fact]
    public void Xlsx_ReadsCellsInRowOrder_NotSharedStringOrder()
    {
        // Shared strings are stored in first-use order; the sheet decides the reading order.
        const string s = "xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"";
        var path = Zip("t.xlsx",
            ("xl/sharedStrings.xml", $"<sst {s}><si><t>&lt;&lt;/foreach&gt;&gt;</t></si><si><t>&lt;&lt;foreach [c in ds]&gt;&gt;</t></si><si><r><t>&lt;&lt;[c.</t></r><r><t>Name]&gt;&gt;</t></r></si></sst>"),
            ("xl/worksheets/sheet1.xml", $"<worksheet {s}><sheetData>" +
                "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>1</v></c></row>" +
                "<row r=\"2\"><c r=\"A2\" t=\"s\"><v>2</v></c><c r=\"B2\" t=\"inlineStr\"><is><t>&lt;&lt;[c.Age]&gt;&gt;</t></is></c><c r=\"C2\"><v>42</v></c></row>" +
                "<row r=\"3\"><c r=\"A3\" t=\"s\"><v>0</v></c></row></sheetData></worksheet>"));

        var part = Assert.Single(TemplateText.Read(path)!);

        Assert.Equal("worksheet 1", part.Name);
        Assert.Equal(["<<foreach [c in ds]>>", "<<[c.Name]>>", "<<[c.Age]>>", "<</foreach>>"], part.Units);
    }

    [Fact]
    public void Pptx_ReadsSlidesByNumber()
    {
        const string a = "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"";
        var path = Zip("t.pptx",
            ("ppt/slides/slide10.xml", $"<p:sld xmlns:p=\"p\" {a}><a:p><a:r><a:t>ten</a:t></a:r></a:p></p:sld>"),
            ("ppt/slides/slide2.xml", $"<p:sld xmlns:p=\"p\" {a}><a:p><a:r><a:t>two</a:t></a:r></a:p></p:sld>"));

        var parts = TemplateText.Read(path)!;

        Assert.Equal(["slide 2", "slide 10"], parts.Select(p => p.Name));
    }

    [Fact]
    public void Odt_ReadsParagraphsAndSpaces()
    {
        var path = Zip("t.odt", ("content.xml",
            "<office:document-content xmlns:office=\"o\" xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\"><office:body><office:text>" +
            "<text:p>&lt;&lt;[a]&gt;&gt;<text:s text:c=\"2\"/><text:span>&lt;&lt;[b]&gt;&gt;</text:span></text:p></office:text></office:body></office:document-content>"));
        Assert.Equal(["<<[a]>>  <<[b]>>"], TemplateText.Read(path)![0].Units);
    }

    [Fact]
    public void Html_DecodesEncodedTags_AndKeepsRawOnes()
    {
        // Upstream HTML templates encode tags; a raw tag typed into the source must survive markup stripping.
        var path = File("t.html", "<html><body><p>&lt;&lt;foreach [in customers]&gt;&gt;</p><style>p{}</style><b><<[CustomerName]>></b>&lt;&lt;/foreach&gt;&gt;</body></html>");
        var text = string.Join("\n", TemplateText.Read(path)![0].Units);
        Assert.Contains("<<foreach [in customers]>>", text);
        Assert.Contains("<<[CustomerName]>>", text);
        Assert.Contains("<</foreach>>", text);
        Assert.DoesNotContain("p{}", text);
    }

    [Fact]
    public void Eml_DecodesQuotedPrintable()
    {
        var path = File("t.eml", "Subject: x\r\nContent-Transfer-Encoding: quoted-printable\r\n\r\nHello =3C=3C[customer.Na=\r\nme]=3E=3E\r\n");
        Assert.Contains("<<[customer.Name]>>", TemplateText.Read(path)![0].Units[0]);
    }

    [Fact]
    public void Rtf_StripsControlWords()
    {
        var path = File("t.rtf", @"{\rtf1\ansi {\b Dear} <<[customer.Name]>>\par}");
        Assert.Contains("<<[customer.Name]>>", TemplateText.Read(path)![0].Units[0]);
    }

    [Theory]
    [InlineData("t.doc")]
    [InlineData("t.xls")]
    [InlineData("t.ppt")]
    [InlineData("t.msg")]
    public void BinaryFormats_AreNotReadable(string name) =>
        Assert.Null(TemplateText.Read(File(name, "binary")));

    [Fact]
    public void MislabelledZip_IsNotReadable() =>
        Assert.Null(TemplateText.Read(File("fake.docx", "not a zip")));

    [Fact]
    public void PlainText_IsOneUnit() =>
        Assert.Equal(["a <<[x]>>\nb"], TemplateText.Read(File("t.md", "a <<[x]>>\nb"))![0].Units);
}
