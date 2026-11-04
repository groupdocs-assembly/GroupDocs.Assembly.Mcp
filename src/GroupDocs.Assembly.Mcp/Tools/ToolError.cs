using System.Text;
using ModelContextProtocol.Protocol;

namespace GroupDocs.Assembly.Mcp.Tools;

/// <summary>
/// Shared result builders for the tool surface.
/// </summary>
/// <remarks>
/// Every tool returns a <see cref="CallToolResult"/> rather than a string, so an engine failure is
/// reported with <c>isError: true</c> AND keeps the readable "&lt;Operation&gt; failed for '&lt;file&gt;': …"
/// prefix as its first line. Clients can branch on one flag; agents and humans can read the cause.
/// (Products that return strings cannot set isError on engine failures — a family-wide defect this
/// server does not inherit.)
/// </remarks>
internal static class ToolError
{
    private const int MaxInnerDepth = 5;

    public static CallToolResult Success(string text) => new()
    {
        Content = [new TextContentBlock { Text = text }]
    };

    public static CallToolResult Failure(string text) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = text }]
    };

    /// <summary>
    /// "&lt;op&gt; failed for '&lt;file&gt;'[ &lt;subject&gt;]: &lt;type&gt;: &lt;message&gt; | inner(0): …", with every
    /// occurrence of the server's temp working folder replaced by the user's file name, so a
    /// failure never leaks a machine path.
    /// </summary>
    public static CallToolResult Failure(string op, string file, Exception ex, string? subject = null, string? hint = null)
        => Failure(Format(op, file, ex, subject, hint));

    public static string Format(string op, string file, Exception ex, string? subject = null, string? hint = null)
    {
        var sb = new StringBuilder();
        sb.Append($"{op} failed for '{file}'");
        if (!string.IsNullOrEmpty(subject)) sb.Append(' ').Append(subject);
        sb.Append(": ");

        // Argument problems the tool itself detected are explained in plain words; engine
        // exceptions keep their type so a support ticket can name it.
        if (ex is ArgumentException or ToolInputException)
            sb.Append(ex.Message);
        else
            sb.Append(ex.GetType().FullName).Append(": ").Append(ex.Message);

        var inner = ex.InnerException;
        for (var d = 0; inner is not null && d < MaxInnerDepth; d++, inner = inner.InnerException)
            sb.Append($" | inner({d}): {inner.GetType().FullName}: {inner.Message}");

        if (!string.IsNullOrEmpty(hint)) sb.Append(' ').Append(hint);

        return TempWorkspace.Scrub(sb.ToString(), file);
    }
}

/// <summary>A caller error the tool detected before touching the engine (bad part number, unsupported target…).</summary>
internal sealed class ToolInputException(string message) : Exception(message);
