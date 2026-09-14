using System.Text.RegularExpressions;
using Ben.Web.Tests.Support;
using Xunit;

namespace Ben.Web.Tests.Blocks;

/// <summary>
/// The block editor stays reusable: nothing in Kit/Blocks knows about cases, research, groups, places or the API.
/// </summary>
/// <remarks>
/// Ben, 2026-09-14: "make this a set of reusable components … so we might be able to use it for more than just research."
/// The easiest way to lose that is one convenient reference to a case or a client call inside a block. Everything the
/// editor needs from its page comes through BlockHostContracts; this keeps it that way. It also holds the map block's
/// promise: with no way to reach a case or a place record, a new map cannot be filled in from the case's address.
/// </remarks>
public sealed class BlockKitIsolationTests
{
    private static readonly Regex Forbidden = new(
        @"\b(Case\w*|Research\w*|Organization\w*|Investigation\w*|Place(Record|Candidate|Dto|Id\b)\w*|StreetAddress\w*|IBen\w*Client|IBenAdminClient|Geocod\w*|MediaUrlBuilder|IMediaUrlBuilder)\b");

    private static IEnumerable<string> KitFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepoFiles.Root().FullName, "Ben.Web.Website.Library", "Kit", "Blocks"))
            .Where(p => p.EndsWith(".razor") || p.EndsWith(".cs") || p.EndsWith(".js"));

    private static string WithoutComments(string source) =>
        Regex.Replace(Regex.Replace(Regex.Replace(source, @"@\*.*?\*@", "", RegexOptions.Singleline), @"/\*.*?\*/", "", RegexOptions.Singleline),
            @"^\s*//.*$|<!--.*?-->|///.*$", "", RegexOptions.Multiline);

    [Fact]
    public void Nothing_in_the_block_kit_names_the_pages_that_use_it()
    {
        var files = KitFiles().ToList();
        Assert.True(files.Count >= 10, $"only {files.Count} files found under Kit/Blocks — the guard reads nothing");

        var offenders = files
            .SelectMany(p => Forbidden.Matches(WithoutComments(File.ReadAllText(p)))
                .Select(m => $"{Path.GetFileName(p)}: {m.Value}"))
            .Where(o => !o.EndsWith(": PlaceUrl") && !o.EndsWith(": PlaceId"))   // the host's own link for a stop that is a listed place
            .ToList();

        Assert.True(offenders.Count == 0, "Kit/Blocks must not know about its hosts:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_block_kit_injects_no_api_client()
    {
        var injects = KitFiles().Where(p => p.EndsWith(".razor"))
            .SelectMany(p => Regex.Matches(File.ReadAllText(p), @"^@inject\s+(\S+)", RegexOptions.Multiline)
                .Select(m => $"{Path.GetFileName(p)}: {m.Groups[1].Value}"))
            .Where(i => !i.EndsWith(": IJSRuntime") && !i.EndsWith(": MapsOptions"))
            .ToList();
        Assert.True(injects.Count == 0, "Kit/Blocks components may inject only the browser and map options:\n  " + string.Join("\n  ", injects));
    }

    [Fact]
    public void The_block_kits_scripts_import_nothing()
    {
        var imports = KitFiles().Where(p => p.EndsWith(".js"))
            .Where(p => Regex.IsMatch(File.ReadAllText(p), @"^\s*import\s", RegexOptions.Multiline))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Empty(imports);
    }
}
