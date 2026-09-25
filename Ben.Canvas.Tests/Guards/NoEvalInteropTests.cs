using System.Text.RegularExpressions;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Guards;

/// <summary>
/// Nothing in the canvas evaluates strings as code.
/// </summary>
/// <remarks>
/// Copied in intent from Ben.Video.Tests/Services/NoEvalInteropTests.cs. An eval call forces
/// <c>unsafe-eval</c> into whatever Content-Security-Policy the host sets, and the canvas renders pasted
/// content, which is exactly where a strict policy earns its keep.
/// </remarks>
public sealed class NoEvalInteropTests
{
    [Fact]
    public void No_interop_call_names_eval()
    {
        var offenders = RepoFiles.UiFiles("*.cs", "*.razor")
            .Where(f => Regex.IsMatch(RepoFiles.ReadWithoutComments(f), @"Invoke(Void)?Async(<[^>]*>)?\(\s*""eval""", RegexOptions.IgnoreCase))
            .Select(RepoFiles.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "These invoke eval through interop:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void No_script_evaluates_strings()
    {
        var scripts = RepoFiles.Files(RepoFiles.EditorWwwroot(), "*.js").Concat(RepoFiles.Files(RepoFiles.HostWwwroot(), "*.js"));
        var offenders = scripts
            .Where(f => Regex.IsMatch(RepoFiles.ReadWithoutComments(f), @"\beval\s*\(|new\s+Function\s*\("))
            .Select(RepoFiles.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "These scripts evaluate strings as code:\n  " + string.Join("\n  ", offenders));
    }
}
