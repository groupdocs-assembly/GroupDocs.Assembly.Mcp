using System.Text.Json;
using GroupDocs.Assembly.Mcp.Tools;
using Xunit;

namespace GroupDocs.Assembly.Mcp.Tests;

/// <summary>
/// The template texts below are the tags of the upstream GroupDocs.Assembly-for-.NET example templates
/// (Examples/Data/Templates), so the inference is checked against templates people actually write.
/// </summary>
public class TemplateAnalyzerTests
{
    private static TemplateAnalysis Analyze(params string[] units) =>
        TemplateAnalyzer.Analyze([new TemplatePart("body", units)]);

    private static string Skeleton(TemplateAnalysis a) => a.Skeleton.ToJsonString();

    [Fact]
    public void NestedImplicitForeach_BuildsNestedLists()
    {
        // "Data destination with nested elements.docx"
        var a = Analyze("<<foreach [in managers]>>", "<<[Name]>>", "<<foreach [in Contract]>>", "<<[Client.Name]>> <<[Price]>>",
            "<</foreach>>", "<</foreach>>");

        Assert.Equal(["managers"], a.DataSourceNames);
        Assert.Equal(["managers[].Name", "managers[].Contract[].Client.Name", "managers[].Contract[].Price"], a.Fields);
        Assert.Equal("""{"managers":[{"Name":"","Contract":[{"Client":{"Name":""},"Price":""}]}]}""", Skeleton(a));
        Assert.Equal(7, a.Tags.Count);
    }

    [Fact]
    public void ForeachVariable_BindsTheElement()
    {
        // "Data table.docx" (note the spaces the template really has)
        var a = Analyze("<<foreach [     c in ds ]>>", "<<[c.Client]>><<[c.Manager]>><<[c.Price]>>", "<</ foreach>>");
        Assert.Equal(["ds"], a.DataSourceNames);
        Assert.Equal(["ds[].Client", "ds[].Manager", "ds[].Price"], a.Fields);
    }

    [Fact]
    public void LambdaOverAList_TypesTheFieldAsNumber_AndFormatsTypeLeaves()
    {
        // "Data destination with nested elements.txt"
        var a = Analyze("<<foreach [in persons]>>Name: <<[Name]>>, Age: <<[Age]>>, <<[Birth]:\"dd.MM.yyyy\">>\n<</foreach>>",
            "Average age: <<[persons.Average(p => p.Age)]>>");

        Assert.Equal(["persons"], a.DataSourceNames);
        Assert.Equal("""{"persons":[{"Name":"","Age":0,"Birth":"2026-01-31T00:00:00"}]}""", Skeleton(a));
    }

    [Fact]
    public void TagSpanningParagraphs_IsRead()
    {
        // "Table row.docx": the foreach is typed over three lines.
        var a = Analyze("<<foreach [group in contracts", ".GroupBy(c => c.Manager)", ".OrderBy(g => g.Key)]>>",
            "<<[group.Key]>> <<[group.Sum(c => c.Price)]>>", "<</foreach>>");

        Assert.Equal(["contracts"], a.DataSourceNames);
        Assert.Equal("""{"contracts":[{"Manager":"","Price":0}]}""", Skeleton(a));
        Assert.Equal("foreach", a.Tags[0].Kind);
    }

    [Fact]
    public void GroupsOfGroups_LambdasSeeAGroup_AndFirstGivesOne()
    {
        var a = Analyze("<<[orders.GroupBy(o => o.Region).Where(g => g.Count() > 1).First().Sum(o => o.Total)]>>");
        Assert.Equal(["orders[].Region", "orders[].Total"], a.Fields);
    }

    [Fact]
    public void ImplicitGroup_BareCallsReadTheGroupedElements_AndKeyIsNotData()
    {
        // "Bubble chart.docx": chart series tags inside a grouped, variable-less foreach.
        var a = Analyze("<<foreach [in orders .GroupBy(c => c.OrderDate.Month)]>>", "<<x [Key]>>", "<<y [Sum(c => c.Price)]>>", "<<size [Count()]>>", "<</foreach>>");

        Assert.Equal(["orders"], a.DataSourceNames);
        Assert.Contains("orders[].OrderDate.Month", a.Fields);
        Assert.Contains("orders[].Price", a.Fields);
        Assert.DoesNotContain(a.Fields, f => f.Contains("Key"));
    }

    [Fact]
    public void VarDefinitions_AreNotData_ButTheirInputsAre()
    {
        // "In-table list with total.xlsx"
        var a = Analyze("<<var [total = 0.0]>>", "<<foreach [in orders]>>", "<<[Customer.CustomerName]>>",
            "<<var [total = total + Price]>>", "<<[total]>>", "<</foreach>>");

        Assert.Equal(["orders"], a.DataSourceNames);
        Assert.Equal(["orders[].Customer.CustomerName", "orders[].Price"], a.Fields);
    }

    [Fact]
    public void TwoRoots_AreBothDataSourceNames()
    {
        // "Multiple data source.odt"
        var a = Analyze("<<foreach [in Customers]>><<[CustomerName]>><</foreach>>", "<<foreach [in products]>><<[ProductName]>><</foreach>>");
        Assert.Equal(["Customers", "products"], a.DataSourceNames);
    }

    [Fact]
    public void FilteringAndElementMethods_KeepTheElementType()
    {
        var a = Analyze("<<foreach [o in orders.Where(x => x.Paid).OrderBy(x => x.Date)]>><<[o.Total]>><</foreach>>",
            "First: <<[orders.First().Number]>>");
        Assert.Equal(["orders[].Paid", "orders[].Date", "orders[].Total", "orders[].Number"], a.Fields);
    }

    [Fact]
    public void StringLiterals_AndKeywords_AreNotNames()
    {
        var a = Analyze("<<barcode [“736192”] -codabar>>", "<<if [false]>><</if>>", "<<[\"customer.Name\"]>>", "<<[\"\"]>>");
        Assert.Empty(a.DataSourceNames);
        Assert.Empty(a.Fields);
    }

    [Fact]
    public void IfCondition_TypesABareFieldAsBoolean_StringMethodsKeepText()
    {
        var a = Analyze("<<if [customer.IsVip]>>VIP<</if>>", "<<[customer.Name.ToUpper()]>>");
        Assert.Equal("""{"customer":{"IsVip":false,"Name":""}}""", Skeleton(a));
    }

    [Fact]
    public void LinkAndImageTags_ReadEveryExpression()
    {
        var a = Analyze("<<link [site.Url][site.Title]>>", "<<image [photo] -fitSize>>");
        Assert.Equal(["site", "photo"], a.DataSourceNames);
        Assert.Equal(["site.Url", "site.Title", "photo"], a.Fields);
    }

    [Fact]
    public void LetterTemplate_GivesAFillableSkeleton()
    {
        var a = Analyze("Dear <<[customer.Name]>>,", "Your order <<[order.Number]>> of <<[order.Total]:\"N2\">> is ready.",
            "<<foreach [i in order.Items]>>- <<[i.Title]>> x <<[i.Qty * 1]>>", "<</foreach>>");
        Assert.Equal("""{"customer":{"Name":""},"order":{"Number":"","Total":0,"Items":[{"Title":"","Qty":0}]}}""", Skeleton(a));
        Assert.Equal(["customer", "order"], a.DataSourceNames);
    }

    [Fact]
    public void MustachePlaceholders_AreFlagged()
    {
        var a = Analyze("Hello, {{Name}}! Today is {{Now:dd MMM yyyy}}.");
        Assert.Empty(a.Tags);
        Assert.Contains(a.Notes, n => n.Contains("2 '{{…}}' placeholder(s)"));
    }

    [Fact]
    public void Parts_HaveTheirOwnScopes()
    {
        var a = TemplateAnalyzer.Analyze([
            new TemplatePart("body", ["<<foreach [in items]>>"]),
            new TemplatePart("footer1", ["<<[Title]>>"]),
        ]);
        // The body's open foreach does not leak into the footer: Title there is a top-level name.
        Assert.Contains("Title", a.DataSourceNames);
        Assert.Equal("footer1", a.Tags[1].Part);
    }

    [Theory]
    [InlineData("<</ foreach>>", "/foreach", null)]
    [InlineData("<<[a.b]:\"N2\">>", "expression", "a.b")]
    [InlineData("<<foreach [x in list.Where(y => y.Ok)]>>", "foreach", "x in list.Where(y => y.Ok)")]
    [InlineData("<<link [u][t]>>", "link", "u")]
    [InlineData("<<next>>", "next", null)]
    public void ParseTag_SplitsKindAndExpression(string tag, string kind, string? expression)
    {
        var parsed = TemplateAnalyzer.ParseTag(tag[2..^2], tag, "body");
        Assert.Equal(kind, parsed.Kind);
        Assert.Equal(expression, parsed.Expression);
    }

    [Fact]
    public void Skeleton_IsValidJsonForAssembleDocument()
    {
        var a = Analyze("<<foreach [c in customers]>><<[c.Name]>><</foreach>>");
        using var doc = JsonDocument.Parse(Skeleton(a));
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("customers").ValueKind);
    }
}
