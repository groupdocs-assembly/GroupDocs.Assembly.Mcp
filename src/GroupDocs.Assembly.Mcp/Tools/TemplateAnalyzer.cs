using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GroupDocs.Assembly.Mcp.Tools;

/// <summary>One template tag as written, e.g. <c>&lt;&lt;foreach [c in customers]&gt;&gt;</c>.</summary>
internal sealed record TemplateTag(string Text, string Kind, string Part, string? Expression);

/// <summary>What a template asks of its data.</summary>
/// <param name="Tags">Every tag in document order.</param>
/// <param name="DataSourceNames">Names the template uses for its data that it does not define itself.</param>
/// <param name="Fields">Member paths read from the data, e.g. "customers[].Orders[].Price" ("[]" marks a list).</param>
/// <param name="Skeleton">A JSON object shaped like the data the template reads, with placeholder values.</param>
/// <param name="Notes">Caveats about the inference.</param>
internal sealed record TemplateAnalysis(
    IReadOnlyList<TemplateTag> Tags,
    IReadOnlyList<string> DataSourceNames,
    IReadOnlyList<string> Fields,
    JsonObject Skeleton,
    IReadOnlyList<string> Notes);

/// <summary>
/// Finds the LINQ Reporting tags of a template and infers the data shape they read. This is a static
/// reading of expressions, not the engine: it follows <c>foreach</c> scopes, lambda parameters, member
/// chains and <c>var</c> aliases, and gives up (silently) on results it cannot type, such as projections.
/// </summary>
internal static class TemplateAnalyzer
{
    private static readonly Regex TagPattern = new(@"<<(?<body>/?\s*[A-Za-z\[].*?)>>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Mustache = new(@"\{\{\s*[A-Za-z_][^{}]*\}\}", RegexOptions.Compiled);

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "true", "false", "null", "new", "in", "is", "as", "typeof", "default", "this", "var",
        "int", "long", "double", "decimal", "float", "string", "bool", "object", "char", "byte", "DateTime",
        "TimeSpan", "Math", "Convert", "String", "Int32", "Double", "Decimal", "Boolean", "IIF",
    };

    /// <summary>Methods that keep the element type of the collection they are called on.</summary>
    private static readonly HashSet<string> Filtering = new(StringComparer.Ordinal)
    {
        "Where", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "Take", "Skip", "TakeWhile",
        "SkipWhile", "Distinct", "Reverse", "ToList", "ToArray", "Concat", "Union", "Except", "Intersect", "AsEnumerable",
    };

    /// <summary>Methods that return one element of the collection.</summary>
    private static readonly HashSet<string> SingleElement = new(StringComparer.Ordinal)
    {
        "First", "FirstOrDefault", "Last", "LastOrDefault", "Single", "SingleOrDefault", "ElementAt", "ElementAtOrDefault",
    };

    /// <summary>Methods that read numbers from the collection's elements.</summary>
    private static readonly HashSet<string> Numeric = new(StringComparer.Ordinal) { "Sum", "Average", "Min", "Max" };

    private static readonly HashSet<string> CollectionMethods = new(StringComparer.Ordinal)
    {
        "Sum", "Average", "Min", "Max", "Count", "LongCount", "Any", "All", "Select", "SelectMany", "GroupBy",
        "Contains", "Aggregate",
    };

    public static TemplateAnalysis Analyze(IReadOnlyList<TemplatePart> parts)
    {
        var tags = new List<TemplateTag>();
        var model = new DataModel();
        var notes = new List<string>();
        var mustache = 0;

        foreach (var part in parts)
        {
            // A tag may span paragraphs ("<<foreach [group in contracts" ↵ ".GroupBy(…)]>>" is common in
            // upstream templates), so match over the whole part, paragraphs joined by line breaks.
            var text = string.Join("\n", part.Units);
            mustache += Mustache.Matches(text).Count;
            var scope = new ScopeStack();
            foreach (Match m in TagPattern.Matches(text))
            {
                var tag = ParseTag(m.Groups["body"].Value, m.Value, part.Name);
                tags.Add(tag);
                Apply(tag, scope, model);
            }
        }

        if (mustache > 0)
            notes.Add($"The template contains {mustache} '{{{{…}}}}' placeholder(s). GroupDocs.Assembly does not process that syntax and leaves it as text; write <<[name]>> instead.");
        if (tags.Count > 0)
            notes.Add("fields and dataSkeleton are inferred from the tags, not from the engine; values computed by Select/GroupBy projections are not followed.");

        return new TemplateAnalysis(tags, model.Roots.ToList(), model.Paths(), model.Skeleton(), notes);
    }

    // ---- tag parsing --------------------------------------------------------------------------

    internal static TemplateTag ParseTag(string body, string text, string part)
    {
        var trimmed = body.Trim();
        if (trimmed.StartsWith('/'))
            return new TemplateTag(text, "/" + Regex.Match(trimmed[1..].TrimStart(), @"^\w*").Value, part, null);
        if (trimmed.StartsWith('['))
            return new TemplateTag(text, "expression", part, Bracketed(trimmed, 0));
        var name = Regex.Match(trimmed, @"^\w+").Value;
        var open = trimmed.IndexOf('[', name.Length);
        return new TemplateTag(text, name, part, open < 0 ? null : Bracketed(trimmed, open));
    }

    /// <summary>The content of the bracket group starting at <paramref name="open"/>, honouring nesting and string literals.</summary>
    private static string Bracketed(string s, int open)
    {
        var depth = 0;
        char? quote = null;
        for (var i = open; i < s.Length; i++)
        {
            var c = s[i];
            if (quote is not null)
            {
                if (c == '\\') { i++; continue; }
                if (c == quote || (quote == '“' && c == '”')) quote = null;
                continue;
            }
            switch (c)
            {
                case '"' or '\'': quote = c; break;
                case '“': quote = '“'; break;
                case '[': depth++; break;
                case ']':
                    if (--depth == 0) return s[(open + 1)..i];
                    break;
            }
        }
        return s[(open + 1)..];
    }

    // ---- scopes -------------------------------------------------------------------------------

    /// <summary>What a name in an expression stands for.</summary>
    private sealed record Binding(string? Path, string? GroupOf = null);

    private sealed class ScopeStack
    {
        private readonly List<(string Kind, Dictionary<string, Binding> Names, Binding? Implicit)> _frames = [("root", new(), null)];

        public void Push(string kind, string? variable, Binding binding)
        {
            var names = new Dictionary<string, Binding>(StringComparer.Ordinal);
            if (variable is not null) names[variable] = binding;
            _frames.Add((kind, names, variable is null ? binding : null));
        }

        public void Pop(string kind)
        {
            for (var i = _frames.Count - 1; i > 0; i--)
                if (_frames[i].Kind == kind) { _frames.RemoveRange(i, _frames.Count - i); return; }
        }

        public void Define(string name, Binding binding) => _frames[^1].Names[name] = binding;

        public Binding? Lookup(string name)
        {
            for (var i = _frames.Count - 1; i >= 0; i--)
                if (_frames[i].Names.TryGetValue(name, out var b)) return b;
            return null;
        }

        /// <summary>
        /// The element of the innermost <c>foreach [in x]</c> without a variable, whose members are in scope by
        /// name. A binding with no path means the element's type is unknown (a projection or a group).
        /// </summary>
        public Binding? ImplicitElement()
        {
            for (var i = _frames.Count - 1; i >= 0; i--)
                if (_frames[i].Implicit is not null) return _frames[i].Implicit;
            return null;
        }
    }

    private static void Apply(TemplateTag tag, ScopeStack scope, DataModel model)
    {
        switch (tag.Kind)
        {
            case "foreach":
            {
                var (variable, source) = SplitForeach(tag.Expression ?? string.Empty);
                var result = new ExpressionReader(source, scope, model).Read();
                // Iterating a list of groups gives one group per step; iterating one group gives its elements.
                var binding = result.GroupOf switch
                {
                    ['*', .. var of] => new Binding(null, of),
                    { } of => new Binding(model.MarkList(of)),
                    null => new Binding(result.Path is null ? null : model.MarkList(result.Path)),
                };
                scope.Push("foreach", variable, binding);
                break;
            }
            case "/foreach":
                scope.Pop("foreach");
                break;
            case "var":
            {
                var expr = tag.Expression ?? string.Empty;
                var eq = expr.IndexOf('=');
                if (eq > 0 && (eq + 1 >= expr.Length || expr[eq + 1] != '='))
                {
                    var name = Regex.Match(expr[..eq], @"(\w+)\s*$").Groups[1].Value;
                    var result = new ExpressionReader(expr[(eq + 1)..], scope, model).Read();
                    if (name.Length > 0) scope.Define(name, new Binding(result.Path, result.GroupOf));
                }
                break;
            }
            case "link":
            {
                // <<link [uri][text]>>: two expressions.
                new ExpressionReader(tag.Expression ?? string.Empty, scope, model).Read();
                var second = tag.Text.IndexOf("][", StringComparison.Ordinal);
                if (second > 0) new ExpressionReader(Bracketed(tag.Text, second + 1), scope, model).Read();
                break;
            }
            case "if" or "elseif":
            {
                var result = new ExpressionReader(tag.Expression ?? string.Empty, scope, model).Read();
                if (result.Path is not null && result.WholeExpression) model.SetType(result.Path, LeafType.Boolean);
                break;
            }
            default:
                if (tag.Expression is not null)
                {
                    var result = new ExpressionReader(tag.Expression, scope, model).Read();
                    var format = Regex.Match(tag.Text, @"\]\s*:\s*""(?<f>[^""]*)""").Groups["f"].Value;
                    if (result.Path is not null && result.WholeExpression && format.Length > 0)
                        model.SetType(result.Path, LooksLikeDateFormat(format) ? LeafType.Date : LeafType.Number);
                }
                break;
        }
    }

    private static bool LooksLikeDateFormat(string format) =>
        Regex.IsMatch(format, "(d{1,4}|M{2,4}|y{2,4}|H{1,2}|h{1,2}|mm|ss)") && !Regex.IsMatch(format, "^[NnCcPpFfEeGg]\\d*$");

    /// <summary>"c in customers.Where(...)" → ("c", "customers.Where(...)"); "in customers" → (null, "customers").</summary>
    private static (string? Variable, string Source) SplitForeach(string expression)
    {
        var m = Regex.Match(expression, @"^\s*(?:(?:[\w.<>]+\s+)?(?<var>\w+)\s+)?in\s+(?<src>.*)$", RegexOptions.Singleline);
        return m.Success
            ? (m.Groups["var"].Success ? m.Groups["var"].Value : null, m.Groups["src"].Value)
            : (null, expression);
    }

    // ---- expressions --------------------------------------------------------------------------

    private sealed record ReadResult(string? Path, string? GroupOf, bool WholeExpression);

    /// <summary>
    /// Walks the tokens of one expression, recording every data member it reads. Returns the path of the
    /// expression when the whole expression is one member chain (so a foreach can bind its element).
    /// </summary>
    private sealed class ExpressionReader(string expression, ScopeStack scope, DataModel model)
    {
        private readonly List<string> _tokens = Tokenize(expression);
        private int _pos;

        private readonly Stack<Dictionary<string, Binding>> _lambdas = new();

        public ReadResult Read()
        {
            ReadResult? first = null;
            var chains = 0;
            while (_pos < _tokens.Count)
            {
                var t = _tokens[_pos];
                if (IsIdentifier(t) && (_pos == 0 || _tokens[_pos - 1] != "."))
                {
                    var start = _pos;
                    var r = ReadChain();
                    chains++;
                    first ??= r;
                    if (r.Path is not null && (IsArithmetic(start - 1) || IsArithmetic(_pos)))
                        model.SetType(r.Path, LeafType.Number, onlyIfUnknown: true);
                    continue;
                }
                _pos++;
            }
            var whole = chains == 1 && first is not null && OnlyChain();
            return first is null ? new ReadResult(null, null, false) : first with { WholeExpression = whole };
        }

        /// <summary>"-", "*", "/" and "%" only take numbers; "+" also joins strings, so it proves nothing.</summary>
        private bool IsArithmetic(int index) => index >= 0 && index < _tokens.Count && _tokens[index] is "-" or "*" or "/" or "%";

        private bool OnlyChain() => !_tokens.Any(t => t is "+" or "-" or "*" or "/" or "%" or "?" or ":" or "==" or "!=" or "<" or ">" or "<=" or ">=" or "&&" or "||" or "!" or ",");

        /// <summary>Reads "a.b.c", "a.b.Where(x => x.c).First().d" and so on, starting at an identifier.</summary>
        private ReadResult ReadChain()
        {
            var head = _tokens[_pos];
            string? path;
            string? groupOf = null;

            // Lambda declaration "x => …": the parameter is bound by the enclosing call, not a data read.
            if (Peek(1) == "=>") { _pos += 2; return new ReadResult(null, null, false); }

            var lambda = LookupLambda(head);
            var bound = lambda ?? scope.Lookup(head);
            var implicitElement = scope.ImplicitElement();
            if (bound is not null)
            {
                path = bound.Path;
                groupOf = bound.GroupOf;
            }
            else if (Keywords.Contains(head) || char.IsDigit(head[0]))
            {
                path = null;
            }
            else if (Peek(1) == "(")
            {
                // A bare call: a method of the implicit element ("Sum(c => c.Price)" inside
                // "foreach [in orders.GroupBy(…)]"), or a template helper.
                _pos++;
                (path, groupOf) = implicitElement is null
                    ? ReadCall(head, null, null)
                    : ReadCall(head, implicitElement.Path, implicitElement.GroupOf);
                return ContinueChain(path, groupOf);
            }
            else if (implicitElement is not null && !model.Roots.Contains(head))
            {
                // A member of the implicit element; unknown when the element's type is unknown.
                path = implicitElement.Path is null ? null : implicitElement.Path + "." + head;
            }
            else
            {
                model.AddRoot(head);
                path = head;
            }
            if (path is not null) model.Touch(path);
            _pos++;
            return ContinueChain(path, groupOf);
        }

        private ReadResult ContinueChain(string? path, string? groupOf)
        {
            while (_pos < _tokens.Count && _tokens[_pos] == "." && _pos + 1 < _tokens.Count && IsIdentifier(_tokens[_pos + 1]))
            {
                var member = _tokens[_pos + 1];
                _pos += 2;
                if (Peek(0) == "(")
                {
                    (path, groupOf) = ReadCall(member, path, groupOf);
                    continue;
                }
                if (groupOf is not null)
                {
                    // A group's Key is the grouping value, not a data member.
                    path = null;
                    groupOf = null;
                    continue;
                }
                if (path is not null)
                {
                    path = path + "." + member;
                    model.Touch(path);
                }
            }
            return new ReadResult(path, groupOf, false);
        }

        /// <summary>
        /// Reads a method call on <paramref name="receiver"/>; returns what the call evaluates to.
        /// <paramref name="groupOf"/> is "X" for one group of X's elements and "*X" for a list of such groups
        /// (the result of <c>X.GroupBy(…)</c>).
        /// </summary>
        private (string? Path, string? GroupOf) ReadCall(string method, string? receiver, string? groupOf)
        {
            var known = Filtering.Contains(method) || SingleElement.Contains(method) || CollectionMethods.Contains(method);
            if (groupOf is not null && groupOf.StartsWith('*'))
            {
                // A list of groups: lambdas see one group; filtering keeps the list, First() gives a group.
                var oneGroup = groupOf[1..];
                ReadArguments(method, new Binding(null, oneGroup));
                if (Filtering.Contains(method)) return (null, groupOf);
                if (SingleElement.Contains(method)) return (null, oneGroup);
                return (null, null);
            }

            var collection = groupOf ?? receiver;
            var isCollection = collection is not null && known;
            string? element = null;
            if (isCollection)
            {
                element = model.MarkList(collection!);
            }
            else if (receiver is not null)
            {
                // A string method or similar: the receiver is a scalar.
                model.SetType(receiver, LeafType.Text, onlyIfUnknown: true);
            }

            ReadArguments(method, new Binding(element));

            if (!isCollection) return (null, null);
            if (Filtering.Contains(method)) return (collection, null);
            if (SingleElement.Contains(method)) return (element, null);
            if (method == "GroupBy") return (null, "*" + collection);
            return (null, null);
        }

        /// <summary>Reads the arguments of a call; a lambda parameter ("x => …") is bound to <paramref name="parameter"/>.</summary>
        private void ReadArguments(string method, Binding parameter)
        {
            _pos++; // '('
            var depth = 1;
            var frame = new Dictionary<string, Binding>(StringComparer.Ordinal);
            _lambdas.Push(frame);
            var numericBody = Numeric.Contains(method);
            while (_pos < _tokens.Count && depth > 0)
            {
                var t = _tokens[_pos];
                if (t == "(") { depth++; _pos++; continue; }
                if (t == ")") { depth--; _pos++; continue; }
                if (IsIdentifier(t) && Peek(1) == "=>" && (_pos == 0 || _tokens[_pos - 1] is "(" or ","))
                {
                    frame[t] = parameter;
                    _pos += 2;
                    continue;
                }
                if (IsIdentifier(t) && (_pos == 0 || _tokens[_pos - 1] != "."))
                {
                    var r = ReadChain();
                    if (numericBody && r.Path is not null) model.SetType(r.Path, LeafType.Number, onlyIfUnknown: true);
                    continue;
                }
                _pos++;
            }
            _lambdas.Pop();
        }

        private Binding? LookupLambda(string name)
        {
            foreach (var frame in _lambdas)
                if (frame.TryGetValue(name, out var b)) return b;
            return null;
        }

        private string? Peek(int offset) => _pos + offset < _tokens.Count ? _tokens[_pos + offset] : null;
    }

    private static bool IsIdentifier(string token) => token.Length > 0 && (char.IsLetter(token[0]) || token[0] == '_');

    private static readonly Regex TokenPattern = new(
        @"""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|“[^”]*”|=>|==|!=|<=|>=|&&|\|\||[A-Za-z_]\w*|\d+(?:\.\d+)?[mMdDfFlL]?|\S",
        RegexOptions.Compiled);

    /// <summary>Tokens of an expression; string literals collapse to "" so their content is never read as names.</summary>
    private static List<string> Tokenize(string expression) =>
        TokenPattern.Matches(expression)
            .Select(m => m.Value[0] is '"' or '\'' or '“' ? "\"\"" : m.Value)
            .ToList();

    // ---- data model ---------------------------------------------------------------------------

    private enum LeafType { Unknown, Text, Number, Boolean, Date }

    /// <summary>The member tree the template reads. A segment ending in "[]" is a list.</summary>
    private sealed class DataModel
    {
        private readonly List<string> _roots = [];
        private readonly Dictionary<string, Node> _rootNodes = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Roots => _roots;

        public void AddRoot(string name)
        {
            if (!_roots.Contains(name)) _roots.Add(name);
        }

        /// <summary>Records that <paramref name="path"/> is read.</summary>
        public void Touch(string path) => NodeAt(path);

        /// <summary>Marks the node at <paramref name="path"/> as a list and returns the path of its element.</summary>
        public string MarkList(string path)
        {
            if (path.EndsWith("[]", StringComparison.Ordinal)) return path;
            NodeAt(path).IsList = true;
            return path + "[]";
        }

        public void SetType(string path, LeafType type, bool onlyIfUnknown = false)
        {
            var node = NodeAt(path);
            if (!onlyIfUnknown || node.Type == LeafType.Unknown) node.Type = type;
        }

        /// <summary>The node for a path like "a.b[].c" — "[]" steps into the list element, which is the same node.</summary>
        private Node NodeAt(string path)
        {
            var segments = path.Split('.');
            Node? node = null;
            foreach (var raw in segments)
            {
                var name = raw.EndsWith("[]", StringComparison.Ordinal) ? raw[..^2] : raw;
                if (node is null)
                {
                    if (!_rootNodes.TryGetValue(name, out node)) _rootNodes[name] = node = new Node();
                    AddRoot(name);
                }
                else
                {
                    if (!node.Children.TryGetValue(name, out var child)) node.Children[name] = child = new Node();
                    node = child;
                }
                if (raw.EndsWith("[]", StringComparison.Ordinal)) node.IsList = true;
            }
            return node!;
        }

        public List<string> Paths()
        {
            var list = new List<string>();
            foreach (var root in _roots)
                if (_rootNodes.TryGetValue(root, out var node)) Collect(root, node, list);
            return list;
        }

        private static void Collect(string prefix, Node node, List<string> list)
        {
            var name = node.IsList ? prefix + "[]" : prefix;
            if (node.Children.Count == 0) list.Add(name);
            foreach (var (child, n) in node.Children) Collect(name + "." + child, n, list);
        }

        public JsonObject Skeleton()
        {
            var obj = new JsonObject();
            foreach (var root in _roots)
                if (_rootNodes.TryGetValue(root, out var node)) obj[root] = Value(node);
            return obj;
        }

        private static JsonNode? Value(Node node)
        {
            JsonNode? item;
            if (node.Children.Count > 0)
            {
                var o = new JsonObject();
                foreach (var (name, child) in node.Children) o[name] = Value(child);
                item = o;
            }
            else
            {
                item = node.Type switch
                {
                    LeafType.Number => JsonValue.Create(0),
                    LeafType.Boolean => JsonValue.Create(false),
                    LeafType.Date => JsonValue.Create("2026-01-31T00:00:00"),
                    _ => JsonValue.Create(string.Empty)
                };
            }
            return node.IsList ? new JsonArray(item) : item;
        }

        private sealed class Node
        {
            public Dictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);
            public bool IsList { get; set; }
            public LeafType Type { get; set; }
        }
    }
}
