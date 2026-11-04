using GroupDocs.Mcp.Core;

namespace GroupDocs.Assembly.Mcp.Tools;

/// <summary>
/// A private temp folder for one tool call. The engine works with file paths, so inputs are copied
/// here under their original name (the extension drives format detection) and outputs are written
/// here before being stored. Deleted on dispose.
/// </summary>
internal sealed class TempWorkspace : IDisposable
{
    private const string Prefix = "gd_mcp_assembly_";

    public string Root { get; }

    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string PathFor(string fileName) => Path.Combine(Root, Path.GetFileName(fileName));

    /// <summary>Copies a resolved input into the workspace and returns its path.</summary>
    public async Task<string> SaveInputAsync(ResolvedFile resolved, string? asName = null)
    {
        var path = PathFor(asName ?? resolved.FileName);
        await using var fs = File.Create(path);
        await resolved.Stream.CopyToAsync(fs);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // Best effort: a handle the engine still holds must not turn a result into an error.
        }
    }

    /// <summary>
    /// Replaces any path inside a workspace folder with the name of the file it points at — the user's
    /// name for it, without the workspace's "data_"/"out_" prefixes — or with <paramref name="fileName"/>
    /// when the path is the folder itself. Engine exceptions quote the full path they were given
    /// ("The parameter is incorrect. : 'C:\…\gd_mcp_assembly_…\x.docx'"), and a tool with two inputs must
    /// name the right one.
    /// </summary>
    public static string Scrub(string text, string fileName)
    {
        var temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pattern = System.Text.RegularExpressions.Regex.Escape(temp) + @"[\\/]" + Prefix + @"[0-9a-f]{32}(?:[\\/](?<rest>[^'""|\r\n]*))?";
        return System.Text.RegularExpressions.Regex.Replace(text, pattern, m =>
        {
            var name = m.Groups["rest"].Value.Replace('\\', '/').Split('/')[0];
            foreach (var prefix in new[] { "data_", "out_" })
                if (name.StartsWith(prefix, StringComparison.Ordinal)) name = name[prefix.Length..];
            return name.Length == 0 ? fileName : name;
        }, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
