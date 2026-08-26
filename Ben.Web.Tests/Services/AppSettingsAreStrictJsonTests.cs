using System.Text.Json;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Every checked-in <c>appsettings*.json</c> must be strict JSON — no <c>//</c> or <c>/* */</c>
/// comments, however common they are elsewhere.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> This repo's own convention for an explanatory note in one of these files is a
/// sibling <c>"_comment..."</c> key, used throughout — see <c>Smtp</c>, <c>AzureAd</c>, <c>Cors</c>
/// and the Serilog section in <c>Ben.Data.WebApi/appsettings.json</c>. A stray <c>//</c> line
/// broke that convention once: item 181 added a <c>MediaTools</c> section with a genuine JS-style
/// comment above it, and <c>scripts/deploy-ishaunted.ps1</c> failed at
/// <c>ConvertFrom-Json</c> on the very first production deploy that touched the file — Windows
/// PowerShell 5.1's cmdlet has no comment tolerance at all. The deploy stopped before publishing
/// anything, so nothing broke in production, but it broke the deploy itself, on a file every
/// contributor edits.</para>
///
/// <para><b>Verified faithfully, not approximately.</b> <see cref="JsonDocument.Parse(string,
/// JsonDocumentOptions)"/> with the default <see cref="JsonCommentHandling.Disallow"/> throws on
/// exactly the same input <c>ConvertFrom-Json</c> does, so a file that fails here would have failed
/// the deploy script too.</para>
/// </remarks>
public sealed class AppSettingsAreStrictJsonTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    public static IEnumerable<object[]> AppSettingsFiles()
    {
        var root = RepoRoot();
        return root.GetFiles("appsettings*.json", SearchOption.AllDirectories)
                   .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                            && !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                            && !f.FullName.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}")
                            && !f.FullName.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}"))
                   .Select(f => new object[] { f.FullName });
    }

    [Theory]
    [MemberData(nameof(AppSettingsFiles))]
    public void Parses_as_strict_JSON_with_comments_disallowed(string path)
    {
        var text = File.ReadAllText(path);

        try
        {
            using var _ = JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            Assert.Fail(
                $"{path} is not strict JSON — this is what broke deploy-ishaunted.ps1's "
              + $"ConvertFrom-Json on item 181's MediaTools section. Use a sibling "
              + $"\"_comment...\" key instead of // or /* */: {ex.Message}");
        }
    }
}
