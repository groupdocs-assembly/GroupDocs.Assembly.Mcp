using System.Text.Json.Nodes;
using GroupDocs.Assembly.Mcp.Tools;
using Xunit;

namespace GroupDocs.Assembly.Mcp.Tests;

public sealed class FormatsAndDataTests : IDisposable
{
    private readonly TempWorkspace _work = new();

    public void Dispose() => _work.Dispose();

    private string Json(string text, string name = "d.json")
    {
        var path = _work.PathFor(name);
        File.WriteAllText(path, text);
        return path;
    }

    private static TemplateAnalysis Template(params string[] units) =>
        TemplateAnalyzer.Analyze([new TemplatePart("body", units)]);

    // ---- formats ------------------------------------------------------------------------------

    [Theory]
    [InlineData("docx", "WordProcessing")]
    [InlineData(".MD", "WordProcessing")]
    [InlineData("htm", "WordProcessing")]
    [InlineData("txt", "WordProcessing")]
    [InlineData("xlsx", "Spreadsheet")]
    [InlineData("ods", "Spreadsheet")]
    [InlineData("pptx", "Presentation")]
    [InlineData("otp", "Presentation")]
    [InlineData("msg", "Email")]
    [InlineData("eml", "Email")]
    [InlineData("pdf", null)]
    [InlineData("json", null)]
    public void FamilyOfExtension(string ext, string? family) =>
        Assert.Equal(family, AssemblyFormats.FamilyOfExtension(ext));

    [Theory]
    [InlineData("WordProcessing", "pdf", FileFormat.Pdf)]
    [InlineData("WordProcessing", "md", FileFormat.Markdown)]
    [InlineData("Spreadsheet", "xlsx", FileFormat.Xlsx)]
    [InlineData("Presentation", "pdf", FileFormat.Pdf)]
    [InlineData("Email", "msg", FileFormat.MsgUnicode)]
    [InlineData("Email", "docx", FileFormat.Docx)]
    public void TargetFormat_InFamily(string family, string target, FileFormat expected) =>
        Assert.Equal(expected, AssemblyFormats.TargetFormat(family, target));

    [Theory]
    // Measured: "The document cannot be saved to the given format." after a full assembly run.
    [InlineData("WordProcessing", "xlsx")]
    [InlineData("Spreadsheet", "docx")]
    [InlineData("Spreadsheet", "txt")]
    [InlineData("Presentation", "docx")]
    [InlineData("Presentation", "md")]
    public void TargetFormat_OutOfFamily_IsRefusedUpFront(string family, string target)
    {
        var ex = Assert.Throws<ToolInputException>(() => AssemblyFormats.TargetFormat(family, target));
        Assert.Contains($"cannot be saved as '{target}'", ex.Message);
    }

    [Theory]
    [InlineData("xyz")]
    [InlineData("csv")] // no family saves to CSV (measured: even a spreadsheet template cannot)
    public void TargetFormat_UnknownExtension_IsRefused(string target) =>
        Assert.Contains("not an output format", Assert.Throws<ToolInputException>(() => AssemblyFormats.TargetFormat(AssemblyFormats.Spreadsheet, target)).Message);

    [Theory]
    [InlineData("json", "json")]
    [InlineData("XML", "xml")]
    [InlineData("csv", "csv")]
    [InlineData("tsv", "csv")]
    [InlineData("xlsx", "table")]
    [InlineData("docx", "table")]
    [InlineData("pptx", "table")]
    [InlineData("md", null)]
    [InlineData("pdf", null)]
    public void DataKindOfExtension(string ext, string? kind) =>
        Assert.Equal(kind, AssemblyFormats.DataKindOfExtension(ext));

    [Fact]
    public void EverySaveFormat_MapsToAnEngineFormat()
    {
        foreach (var family in AssemblyFormats.Families)
            foreach (var target in AssemblyFormats.SaveFormats(family))
                AssemblyFormats.TargetFormat(family, target);
    }

    // ---- JSON shape -----------------------------------------------------------------------------

    [Fact]
    public void JsonShape_ObjectKeys()
    {
        var (isObject, keys, refs) = AssemblyData.JsonShape(Json("""{ "customer": {}, "items": [] }"""));
        Assert.True(isObject);
        Assert.Equal(["customer", "items"], keys.Order());
        Assert.False(refs);
    }

    [Fact]
    public void JsonShape_Array() => Assert.False(AssemblyData.JsonShape(Json("[1,2]")).IsObject);

    [Fact]
    public void JsonShape_DetectsJsonNetReferences() =>
        Assert.True(AssemblyData.JsonShape(Json("""[{ "$id": "1", "x": { "$ref": "1" } }]""")).References);

    [Fact]
    public void JsonShape_RelaxedJsonFallsBackToTheFirstCharacter()
    {
        // The upstream Managers.json uses unquoted keys, which the engine reads and System.Text.Json does not.
        var (isObject, keys, _) = AssemblyData.JsonShape(Json("[ { Name:\"John\" } ]"));
        Assert.False(isObject);
        Assert.Empty(keys);
    }

    [Theory]
    [InlineData("\"hello\"")]
    [InlineData("42")]
    [InlineData("   ")]
    public void JsonShape_BareValuesAndEmpty_AreRefused(string json) =>
        Assert.Throws<ToolInputException>(() => AssemblyData.JsonShape(Json(json)));

    // ---- data source names -----------------------------------------------------------------------

    [Fact]
    public void Object_WhoseKeysAreTheTemplatesNames_IsUnnamed()
    {
        var loaded = AssemblyData.Load(AssemblyFormats.JsonData, Json("""{"customer":{"Name":"A"}}"""), null,
            Template("<<[customer.Name]>>"), "t.docx", new TableOptions());
        Assert.Null(loaded.Source.Name);
        Assert.Equal("JSON object", loaded.Description);
    }

    [Fact]
    public void Object_ThatTheTemplateCallsByOneName_GetsThatName()
    {
        var loaded = AssemblyData.Load(AssemblyFormats.JsonData, Json("""{"customer":{"Name":"A"}}"""), null,
            Template("<<[order.customer.Name]>>"), "t.docx", new TableOptions());
        Assert.Equal("order", loaded.Source.Name);
    }

    [Fact]
    public void Array_GetsTheTemplatesOnlyName()
    {
        var loaded = AssemblyData.Load(AssemblyFormats.JsonData, Json("""[{"Name":"A"}]"""), null,
            Template("<<foreach [p in persons]>><<[p.Name]>><</foreach>>"), "t.docx", new TableOptions());
        Assert.Equal("persons", loaded.Source.Name);
        Assert.Equal("JSON array as 'persons'", loaded.Description);
    }

    [Fact]
    public void Array_WithSeveralTemplateNames_AsksWhichOne()
    {
        var ex = Assert.Throws<ToolInputException>(() => AssemblyData.Load(AssemblyFormats.JsonData, Json("[{}]"), null,
            Template("<<foreach [in a]>><</foreach>><<foreach [in b]>><</foreach>>"), "t.docx", new TableOptions()));
        Assert.Contains("several data sources (a, b)", ex.Message);
        Assert.Contains("dataSourceName", ex.Message);
    }

    [Fact]
    public void Array_WithAnUnreadableTemplate_AsksForTheName()
    {
        var ex = Assert.Throws<ToolInputException>(() => AssemblyData.Load(AssemblyFormats.JsonData, Json("[{}]"), null,
            null, "t.doc", new TableOptions()));
        Assert.Contains("cannot be read", ex.Message);
        Assert.Contains("dataSourceName", ex.Message);
    }

    [Fact]
    public void ExplicitName_Wins()
    {
        var loaded = AssemblyData.Load(AssemblyFormats.JsonData, Json("[{}]"), "rows", null, "t.doc", new TableOptions());
        Assert.Equal("rows", loaded.Source.Name);
    }

    [Theory]
    [InlineData("1abc")]
    [InlineData("a b")]
    [InlineData("a.b")]
    public void InvalidName_IsRefused(string name) =>
        Assert.Throws<ToolInputException>(() => AssemblyData.Load(AssemblyFormats.JsonData, Json("{}"), name, null, "t.docx", new TableOptions()));

    [Fact]
    public void Csv_IsNamedAndDescribed()
    {
        var csv = Json("Name;Age\nAnn;30\n", "p.csv");
        var loaded = AssemblyData.Load(AssemblyFormats.CsvData, csv, null,
            Template("<<foreach [p in persons]>><<[p.Name]>><</foreach>>"), "t.txt", new TableOptions(Delimiter: ";"));
        Assert.Equal("persons", loaded.Source.Name);
        Assert.StartsWith("CSV with a header row", loaded.Description);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("::")]
    public void Csv_DelimiterMustBeOneCharacter(string delimiter) =>
        Assert.Throws<ToolInputException>(() => AssemblyData.Load(AssemblyFormats.CsvData, Json("a\n1", "p.csv"), "p", null, "t.txt", new TableOptions(Delimiter: delimiter)));

    [Fact]
    public void Table_IsOneBased() =>
        Assert.Contains("1-based", Assert.Throws<ToolInputException>(() =>
            AssemblyData.Load(AssemblyFormats.TableData, Json("x", "d.xlsx"), "t", null, "t.docx", new TableOptions(Table: 0))).Message);

    // ---- per-record --------------------------------------------------------------------------------

    [Fact]
    public void Records_RootArray() => Assert.Equal(2, AssemblePerRecordTool.Records("[{},{}]", null).Count);

    [Fact]
    public void Records_TheObjectsOnlyArray() =>
        Assert.Equal(3, AssemblePerRecordTool.Records("""{"title":"x","customers":[{},{},{}]}""", null).Count);

    [Fact]
    public void Records_DottedPath() =>
        Assert.Single(AssemblePerRecordTool.Records("""{"batch":{"items":[{}]},"other":[1,2]}""", "batch.items"));

    [Theory]
    [InlineData("""{"a":[1],"b":[2]}""", null, "several arrays (a, b)")]
    [InlineData("""{"a":1}""", null, "holds no array")]
    [InlineData("""{"a":[1]}""", "b", "does not exist")]
    [InlineData("""{"a":{"b":1}}""", "a.b", "is not an array")]
    [InlineData("""not json""", null, "not valid JSON")]
    [InlineData("42", null, "must be a JSON array")]
    public void Records_Errors(string json, string? path, string expected) =>
        Assert.Contains(expected, Assert.Throws<ToolInputException>(() => AssemblePerRecordTool.Records(json, path)).Message);

    [Fact]
    public void FileStem_FromADottedField_AndSanitised()
    {
        var record = JsonNode.Parse("""{"customer":{"Name":"Ann/Lee"},"n":7}""")!.AsObject();
        Assert.Equal("Ann_Lee", AssemblePerRecordTool.FileStem(record, "customer.Name"));
        Assert.Equal("7", AssemblePerRecordTool.FileStem(record, "n"));
        Assert.Null(AssemblePerRecordTool.FileStem(record, null));
        Assert.Contains("object or a list", Assert.Throws<ToolInputException>(() => AssemblePerRecordTool.FileStem(record, "customer")).Message);
        Assert.Contains("no 'nope' field", Assert.Throws<ToolInputException>(() => AssemblePerRecordTool.FileStem(record, "nope")).Message);
    }
}
