using System.Text.RegularExpressions;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Guards;

/// <summary>
/// Every icon name the canvas asks for exists in the shipped sprite.
/// </summary>
/// <remarks>
/// A missing symbol renders an empty &lt;svg&gt;, which defaults to 300 by 150 pixels and shoves a toolbar
/// apart. The sprite has no undo, redo, pencil or group symbols: use rotate-ccw, rotate-cw, edit-3 and layers.
/// </remarks>
public sealed class IconNameGuardTests
{
    private static HashSet<string> Symbols()
    {
        var sprite = Path.Combine(RepoFiles.HostWwwroot(), "icons", "sprite.svg");
        Assert.True(File.Exists(sprite), $"The sprite is missing: {sprite}");

        return Regex.Matches(File.ReadAllText(sprite), @"<symbol[^>]*\bid=""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void Every_icon_name_exists_in_the_sprite()
    {
        var symbols = Symbols();
        Assert.NotEmpty(symbols);

        var missing = new List<string>();

        foreach (var file in RepoFiles.UiFiles("*.razor"))
            foreach (Match m in Regex.Matches(RepoFiles.ReadWithoutComments(file), @"<BcIcon\b[^>]*\bName=""([^""@]+)"""))
                if (!symbols.Contains(m.Groups[1].Value))
                    missing.Add($"{RepoFiles.Relative(file)}: {m.Groups[1].Value}");

        foreach (var file in RepoFiles.UiFiles("*.cs"))
            foreach (Match m in Regex.Matches(RepoFiles.ReadWithoutComments(file), @"\bIcon(Name)?\s*=\s*""([a-z0-9-]+)"""))
                if (!symbols.Contains(m.Groups[2].Value))
                    missing.Add($"{RepoFiles.Relative(file)}: {m.Groups[2].Value}");

        // BcToast's level icons are expressions; pin them here so a renamed symbol still fails.
        foreach (var name in new[] { "check-circle", "alert-triangle", "alert-octagon", "info" })
            if (!symbols.Contains(name))
                missing.Add($"BcToast: {name}");

        Assert.True(missing.Count == 0, "These icon names are not in icons/sprite.svg:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void Every_block_icon_exists_in_the_sprite()
    {
        var symbols = Symbols();
        var names = Ben.Canvas.Core.Blocks.BlockRegistry.All.Select(d => d.IconName)
            .Append(Ben.Canvas.Core.Blocks.BlockRegistry.GroupIconName);

        var missing = names.Where(n => !symbols.Contains(n)).ToList();
        Assert.True(missing.Count == 0, "These block icons are not in icons/sprite.svg: " + string.Join(", ", missing));
    }
    /// <summary>
    /// And the template pictures. These live in Core, which the source scan above does not reach — it
    /// walks the editor library and the host only — so they are read from the catalogue itself.
    /// </summary>
    [Fact]
    public void Every_template_icon_exists_in_the_sprite()
    {
        var symbols = Symbols();
        var missing = Ben.Canvas.Core.Templates.BoardTemplates.All
            .Where(t => !symbols.Contains(t.IconName))
            .Select(t => $"{t.Id}: {t.IconName}")
            .ToList();

        Assert.True(missing.Count == 0, "These template icons are not in icons/sprite.svg: " + string.Join(", ", missing));
    }
}
