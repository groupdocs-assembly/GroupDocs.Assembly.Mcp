using GroupDocs.Assembly.Mcp.Tools;
using ModelContextProtocol.Protocol;
using Xunit;

namespace GroupDocs.Assembly.Mcp.Tests;

public class ToolErrorTests
{
    private static string Text(CallToolResult r) => Assert.IsType<TextContentBlock>(Assert.Single(r.Content)).Text;

    private static TemplateAnalysis Analysis(string text) =>
        TemplateAnalyzer.Analyze([new TemplatePart("body", [text])]);

    [Fact]
    public void Failure_SetsIsError_AndStartsWithThePrefix()
    {
        var result = ToolError.Failure("Document assembly", "invoice.docx",
            new InvalidOperationException("boom", new IOException("disk")));

        Assert.True(result.IsError);
        var text = Text(result);
        Assert.StartsWith("Document assembly failed for 'invoice.docx': System.InvalidOperationException: boom", text);
        Assert.Contains("inner(0): System.IO.IOException: disk", text);
    }

    [Fact]
    public void Failure_CallerErrors_ArePlainWords()
    {
        var text = Text(ToolError.Failure("Document assembly", "a.docx", new ToolInputException("no data was given.")));
        Assert.Equal("Document assembly failed for 'a.docx': no data was given.", text);
    }

    [Fact]
    public void Success_DoesNotSetIsError() =>
        Assert.NotEqual(true, ToolError.Success("ok").IsError);

    [Fact]
    public void Hint_DeniedWrite_NamesTheDockerUserFix()
    {
        var denied = new UnauthorizedAccessException("Access to the path '/data/x.docx' is denied.");
        var hint = AssemblyTool.Hint(denied);
        Assert.Contains("--user \"$(id -u):$(id -g)\"", hint);
        Assert.Contains("GROUPDOCS_MCP_OUTPUT_PATH", hint);
    }

    [Fact]
    public void Hint_MissingField_NamesItAndOffersAllowMissingMembers()
    {
        // Measured engine text for <<[customer.Nope]>> when the data has no Nope.
        var ex = new InvalidOperationException(
            "An error has been encountered at the end of expression 'customer.Nope]>'. Can not get the value of member 'Nope' on type ''.");
        var hint = AssemblyTool.Hint(ex, Analysis("<<[customer.Nope]>>"));
        Assert.Contains("reads 'Nope'", hint);
        Assert.Contains("allowMissingMembers=true", hint);
        Assert.Contains("empty", hint);
    }

    [Fact]
    public void Hint_UnknownDataName_SaysToPassTheDataUnderThatName()
    {
        // Measured engine text when the data source is not named as the template expects.
        var ex = new InvalidOperationException(
            "An error has been encountered at the end of expression 'p in persons]>'. Can not get the value of member 'persons' on type 'System.Data.DataRowCollection'.");
        var hint = AssemblyTool.Hint(ex, Analysis("<<foreach [p in persons]>><<[p.Name]>><</foreach>>"));
        Assert.Contains("'dataSourceName'='persons'", hint);
    }

    [Fact]
    public void Hint_EmptyListField_IsAFieldProblem_NotANameProblem()
    {
        // The same "on type 'System.Data.DataRow'" text appears when a list is empty in the data.
        var ex = new InvalidOperationException(
            "An error has been encountered at the end of expression 'i.Title]>'. Can not get the value of member 'Title' on type 'System.Data.DataRow'.");
        var hint = AssemblyTool.Hint(ex, Analysis("<<foreach [i in order.Items]>><<[i.Title]>><</foreach>>"));
        Assert.DoesNotContain("dataSourceName", hint);
        Assert.Contains("allowMissingMembers=true", hint);
    }

    [Fact]
    public void Hint_UnclosedBlock_NamesTheClosingTags() =>
        Assert.Contains("<</foreach>>", AssemblyTool.Hint(new InvalidOperationException("Unexpected end of template.")));

    [Fact]
    public void Hint_GdiPlusOnLinux_NamesLibgdiplus()
    {
        var ex = new InvalidOperationException("The type initializer for 'Gdip' threw an exception.",
            new TypeInitializationException("Gdip", new PlatformNotSupportedException("System.Drawing.Common is not supported on non-Windows platforms.")));
        Assert.Contains("libgdiplus", AssemblyTool.Hint(ex));
    }

    [Fact]
    public void Hint_ClearEngineMessages_GetNoHint() =>
        Assert.Null(AssemblyTool.Hint(new IOException("disk full")));

    [Fact]
    public void Scrub_ReplacesWorkspacePaths_WithTheFileName()
    {
        using var work = new TempWorkspace();
        var leaked = $"Cannot read '{work.PathFor("data_customers.json")}' for '{work.PathFor("out_letter_assembled.docx")}' (in '{work.Root}')";

        var scrubbed = TempWorkspace.Scrub(leaked, "letter.docx");

        Assert.Equal("Cannot read 'customers.json' for 'letter_assembled.docx' (in 'letter.docx')", scrubbed);
        Assert.DoesNotContain(Path.GetTempPath(), scrubbed);
    }

    [Fact]
    public void Workspace_IsDeletedOnDispose()
    {
        string root;
        using (var work = new TempWorkspace())
        {
            root = work.Root;
            File.WriteAllText(work.PathFor("x.txt"), "x");
        }
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("out.docx", "docx", "out.docx")]
    [InlineData("out", "pdf", "out.pdf")]
    [InlineData("sub/out.txt", "pdf", "out.pdf")]
    [InlineData(null, "pdf", null)]
    public void CallerOutputName_ForcesTheTargetExtension(string? name, string ext, string? expected) =>
        Assert.Equal(expected, AssemblyTool.CallerOutputName(name, ext));

    [Fact]
    public void CallerOutputName_RejectsInvalidNames() =>
        Assert.Throws<ToolInputException>(() => AssemblyTool.CallerOutputName("a\0b", "pdf"));

    [Fact]
    public void OutputName_UsesBaseNameSuffixAndExtension() =>
        Assert.Equal("invoice_assembled.pdf", AssemblyTool.OutputName("invoice.docx", "_assembled", ".PDF"));

    [Theory]
    [InlineData("Ann Lee", "Ann Lee")]
    [InlineData("A/2:x", "A_2_x")]
    [InlineData("  ..hidden. ", "hidden")]
    public void SafeFileName_ReplacesPathCharacters(string value, string expected) =>
        Assert.Equal(expected, AssemblyTool.SafeFileName(value));

    [Fact]
    public void Switches_AlwaysUseSpreadsheetDataTypes_AndMapEachFlag()
    {
        Assert.Equal(DocumentAssemblyOptions.UseSpreadsheetDataTypes, new AssemblySwitches().ToOptions());
        var all = new AssemblySwitches(true, true, true, true).ToOptions();
        Assert.True(all.HasFlag(DocumentAssemblyOptions.AllowMissingMembers));
        Assert.True(all.HasFlag(DocumentAssemblyOptions.RemoveEmptyParagraphs));
        Assert.True(all.HasFlag(DocumentAssemblyOptions.UpdateFieldsAndFormulas));
        Assert.True(all.HasFlag(DocumentAssemblyOptions.InlineErrorMessages));
    }
}
