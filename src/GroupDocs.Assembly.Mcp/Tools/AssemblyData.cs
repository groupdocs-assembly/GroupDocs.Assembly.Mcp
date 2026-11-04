using System.Text.Json;
using System.Text.RegularExpressions;
using GroupDocs.Assembly.Data;

namespace GroupDocs.Assembly.Mcp.Tools;

/// <summary>The data a template is filled with, loaded and named for the engine.</summary>
/// <param name="Source">What to pass to <see cref="DocumentAssembler.AssembleDocument(string, string, LoadSaveOptions, DataSourceInfo[])"/>.</param>
/// <param name="Description">How the data was read, for the response ("JSON object", "CSV, 3 columns"…).</param>
internal sealed record LoadedData(DataSourceInfo Source, string Description);

/// <summary>Options for reading tabular data (CSV and document tables).</summary>
internal sealed record TableOptions(bool HasHeaders = true, string? Delimiter = null, int? Table = null);

/// <summary>
/// Loads template data from a file or inline JSON and works out the name the template uses for it.
/// </summary>
/// <remarks>
/// How the engine binds data (measured on 26.7.0, build-out plan section 8.3):
/// <list type="bullet">
/// <item>A JSON <b>object</b> loaded with <c>AlwaysGenerateRootObject</c> exposes its keys by name whether or
/// not the data source is named, so <c>{"customer":…,"items":[…]}</c> fills <c>&lt;&lt;[customer.Name]&gt;&gt;</c>.
/// Without that flag an object with a single list property collapses into the list and the key is lost.</item>
/// <item>A JSON <b>array</b>, an XML document, a CSV file or a document table is a collection that the
/// template reaches only through the data source's name (<c>&lt;&lt;foreach [c in customers]&gt;&gt;</c>).</item>
/// <item>JSON values are parsed in <c>Strict</c> mode: the engine's default (Loose) turns "007" into 7 and
/// "+458789" into a number, corrupting codes, IDs and phone numbers. Dates are recognised either way. Data with
/// Json.NET "$id"/"$ref" metadata is the exception: the engine's Strict mode cannot read it, so it is read Loose.</item>
/// </list>
/// </remarks>
internal static class AssemblyData
{
    private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public static LoadedData Load(string kind, string path, string? dataSourceName, TemplateAnalysis? template,
        string templateFile, TableOptions table)
    {
        if (dataSourceName is not null && !Identifier.IsMatch(dataSourceName))
            throw new ToolInputException($"'dataSourceName' '{dataSourceName}' is not a valid name: use letters, digits and '_', starting with a letter.");

        switch (kind)
        {
            case AssemblyFormats.JsonData:
            {
                var (isObject, keys, references) = JsonShape(path);
                var options = new JsonDataLoadOptions
                {
                    // Strict keeps "007" a string. The engine's Strict mode cannot read Json.NET reference
                    // metadata ("$id"/"$ref": KeyNotFoundException 'id'), so such data falls back to Loose.
                    SimpleValueParseMode = references ? JsonSimpleValueParseMode.Loose : JsonSimpleValueParseMode.Strict,
                    AlwaysGenerateRootObject = isObject,
                };
                var name = dataSourceName ?? (isObject
                    ? NameForObject(template, keys)
                    : NameForCollection(template, templateFile, "a JSON array"));
                return new LoadedData(new DataSourceInfo(new JsonDataSource(path, options), name),
                    (isObject ? "JSON object" : "JSON array") + Named(name));
            }
            case AssemblyFormats.XmlData:
            {
                var name = dataSourceName ?? NameForCollection(template, templateFile, "an XML document");
                return new LoadedData(new DataSourceInfo(new XmlDataSource(path), name), "XML" + Named(name));
            }
            case AssemblyFormats.CsvData:
            {
                var options = new CsvDataLoadOptions(table.HasHeaders) { Delimiter = Delimiter(table.Delimiter, path) };
                var name = dataSourceName ?? NameForCollection(template, templateFile, "a CSV file");
                return new LoadedData(new DataSourceInfo(new CsvDataSource(path, options), name),
                    $"CSV{(table.HasHeaders ? " with a header row" : " without headers (columns Column1, Column2, …)")}" + Named(name));
            }
            case AssemblyFormats.TableData:
            {
                var index = table.Table ?? 1;
                if (index < 1) throw new ToolInputException($"'dataTable' is 1-based; {index} is not a table number.");
                DocumentTable documentTable;
                try
                {
                    documentTable = new DocumentTable(path, index - 1, new DocumentTableOptions { FirstRowContainsColumnNames = table.HasHeaders });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("is not found", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ToolInputException($"there is no table {index} in the data document (tables and worksheets are numbered from 1).");
                }
                var columns = documentTable.Columns.Cast<DocumentTableColumn>().Select(c => c.Name).ToList();
                var name = dataSourceName ?? NameForCollection(template, templateFile, "a table");
                return new LoadedData(new DataSourceInfo(documentTable, name),
                    $"table {index} ({columns.Count} column(s): {string.Join(", ", columns.Take(12))}{(columns.Count > 12 ? ", …" : "")})" + Named(name));
            }
            default:
                throw new ToolInputException("the data must be JSON, XML, CSV/TSV, or a spreadsheet, Word or presentation document with a table.");
        }
    }

    private static string Named(string? name) => name is null ? string.Empty : $" as '{name}'";

    private static char Delimiter(string? delimiter, string path)
    {
        if (string.IsNullOrEmpty(delimiter))
            return AssemblyFormats.Normalize(Path.GetExtension(path)) == "tsv" ? '\t' : ',';
        if (delimiter is "\\t" or "tab") return '\t';
        if (delimiter.Length != 1) throw new ToolInputException($"'csvDelimiter' must be one character (or 'tab'), not '{delimiter}'.");
        return delimiter[0];
    }

    /// <summary>
    /// A JSON object's keys are in scope by name. The data source needs a name only when the template
    /// calls the whole object something that is not one of its keys (<c>&lt;&lt;[order.customer.Name]&gt;&gt;</c>
    /// with <c>{"customer":…}</c> → "order").
    /// </summary>
    private static string? NameForObject(TemplateAnalysis? template, IReadOnlyCollection<string> keys)
    {
        if (template is null || keys.Count == 0) return null;
        var unknown = template.DataSourceNames.Where(n => !keys.Contains(n)).ToList();
        return unknown.Count == 1 && template.DataSourceNames.Count == 1 ? unknown[0] : null;
    }

    /// <summary>A collection is reachable only by name: use the one name the template reads data through.</summary>
    private static string? NameForCollection(TemplateAnalysis? template, string templateFile, string what)
    {
        if (template is null)
            throw new ToolInputException(
                $"the data is {what}, which the template reaches by name, and the tags of '{templateFile}' cannot be read to find that name. " +
                "Pass 'dataSourceName' (the name used in tags like <<foreach [c in NAME]>>), or pass the data as a JSON object.");
        return template.DataSourceNames.Count switch
        {
            0 => null,
            1 => template.DataSourceNames[0],
            _ => throw new ToolInputException(
                $"the template reads several data sources ({string.Join(", ", template.DataSourceNames)}) and the data is {what}, which can be only one of them. " +
                "Pass 'dataSourceName', or pass one JSON object with a key per name.")
        };
    }

    /// <summary>
    /// Whether the JSON root is an object, its keys, and whether it carries Json.NET reference metadata.
    /// Falls back to the first character for JSON the engine accepts but System.Text.Json does not
    /// (unquoted keys, as in the upstream samples).
    /// </summary>
    internal static (bool IsObject, IReadOnlyCollection<string> Keys, bool References) JsonShape(string path)
    {
        var text = File.ReadAllText(path).TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (text.Length == 0) throw new ToolInputException("the JSON data is empty.");
        if (text[0] is not ('{' or '['))
            throw new ToolInputException("the JSON data must be an object ({…}) or an array ([…]); GroupDocs.Assembly cannot read a bare value.");
        var references = Regex.IsMatch(text, @"[""']?\$(id|ref)[""']?\s*:");
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? (true, doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal), references)
                : (false, [], references);
        }
        catch (JsonException)
        {
            return (text[0] == '{', [], references);
        }
    }
}
