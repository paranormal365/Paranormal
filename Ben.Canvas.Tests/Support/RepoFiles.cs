using System.Text.RegularExpressions;

namespace Ben.Canvas.Tests.Support;

/// <summary>
/// Finds the canvas projects' source files for the source-scanning guards.
/// </summary>
/// <remarks>
/// Every guard gets its file list from here. A guard that walks up to the wrong marker silently scans
/// nothing and passes forever, so <see cref="Root"/> throws with a sentence naming the marker instead of
/// returning an empty tree.
/// </remarks>
public static class RepoFiles
{
    /// <summary>The folder that holds the Ben.Canvas.Editor project - Messenger today, the IsHaunted repo after the move.</summary>
    public static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Ben.Canvas.Editor")))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException(
                $"No folder containing Ben.Canvas.Editor was found above {AppContext.BaseDirectory}; the guards would scan nothing.");
    }

    public static string EditorRoot() => Path.Combine(Root(), "Ben.Canvas.Editor");
    public static string CoreRoot() => Path.Combine(Root(), "Ben.Canvas.Core");
    public static string HostRoot() => Path.Combine(Root(), "Ben.Wasm.Canvas");
    public static string HostWwwroot() => Path.Combine(HostRoot(), "wwwroot");
    public static string EditorWwwroot() => Path.Combine(EditorRoot(), "wwwroot");

    /// <summary>Files under <paramref name="root"/> matching any pattern, skipping bin, obj and node_modules.</summary>
    public static IReadOnlyList<string> Files(string root, params string[] patterns)
    {
        if (!Directory.Exists(root)) return [];

        var sep = Path.DirectorySeparatorChar;
        return patterns
            .SelectMany(p => Directory.EnumerateFiles(root, p, SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{sep}bin{sep}") && !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}node_modules{sep}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Source files in both canvas UI projects (the editor library and the host).</summary>
    public static IReadOnlyList<string> UiFiles(params string[] patterns)
        => Files(EditorRoot(), patterns).Concat(Files(HostRoot(), patterns)).ToList();

    /// <summary>A path relative to <see cref="Root"/>, for readable failure messages.</summary>
    public static string Relative(string path) => Path.GetRelativePath(Root(), path);

    /// <summary>
    /// Removes comments so a guard does not fire on its own explanation. Five guards in the IsHaunted
    /// codebase have. The lookbehind keeps <c>accept="image/*"</c> from opening a comment.
    /// </summary>
    public static string StripComments(string text, string kind)
    {
        switch (kind)
        {
            case "razor":
                text = Regex.Replace(text, @"@\*.*?\*@", " ", RegexOptions.Singleline);
                text = Regex.Replace(text, "<!--.*?-->", " ", RegexOptions.Singleline);
                return text;

            case "html":
                return Regex.Replace(text, "<!--.*?-->", " ", RegexOptions.Singleline);

            case "css":
                return Regex.Replace(text, @"(?<![\w""'])/\*.*?\*/", " ", RegexOptions.Singleline);

            case "js":
            case "cs":
                text = Regex.Replace(text, @"(?<![\w""'])/\*.*?\*/", " ", RegexOptions.Singleline);
                // Line comments, but not "//" inside a URL such as https://.
                return Regex.Replace(text, @"(?<![:""'\w])//[^\n]*", " ");

            case "xml":
                return Regex.Replace(text, "<!--.*?-->", " ", RegexOptions.Singleline);

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown comment syntax.");
        }
    }

    /// <summary>The comment syntax for a file, from its extension.</summary>
    public static string KindOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".razor" => "razor",
        ".html" => "html",
        ".css" => "css",
        ".js" => "js",
        ".cs" => "cs",
        ".config" or ".xml" or ".svg" => "xml",
        var other => throw new ArgumentOutOfRangeException(nameof(path), other, "No comment syntax known for this extension."),
    };

    /// <summary>The file's text with comments removed.</summary>
    public static string ReadWithoutComments(string path) => StripComments(File.ReadAllText(path), KindOf(path));
}
