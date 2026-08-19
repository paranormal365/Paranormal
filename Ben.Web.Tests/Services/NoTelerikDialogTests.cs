using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Keeps <c>TelerikDialog</c> out of the codebase.
/// </summary>
/// <remarks>
/// <para>Content inside a <c>TelerikDialog</c> does not deliver input events back to Blazor. Proved
/// by an A/B on a live page: two identical plain-Blazor bound inputs, typed the same way, one
/// inside the dialog and one outside — the outside one updated server state, the inside one never
/// did while its own DOM held the typed text.</para>
///
/// <para>The consequence is silent and severe. A Save button gated on something typed above it can
/// never enable, so the workflow is simply impossible, with no error and nothing in any log. It
/// blocked creating calendar events and lookup types for as long as those screens existed. Clicks
/// <i>do</i> cross the boundary, which is what made it look for so long like a button problem, a
/// binding problem, or a broken test harness.</para>
///
/// <para>Use <c>TelerikWindow</c>, with the action buttons inside <c>WindowContent</c> rather than
/// <c>WindowActions</c> — that slot has its own detached-rendering problem (item #68).</para>
/// </remarks>
public sealed class NoTelerikDialogTests
{
    [Fact]
    public void No_razor_file_uses_TelerikDialog()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Ben.slnx")))
            root = root.Parent;
        Assert.NotNull(root);

        var offenders = new List<string>();
        var scanned = 0;

        var files = new[] { "Ben.Web.Website.Library", "Ben.Web.Website" }
            .Select(p => Path.Combine(root!.FullName, p))
            .Where(Directory.Exists)
            .SelectMany(p => Directory.EnumerateFiles(p, "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        foreach (var file in files)
        {
            scanned++;
            var text = File.ReadAllText(file);

            // The opening tag, not the bare word — the explanatory comments left at the old call
            // sites name the control on purpose and must not trip this.
            if (text.Contains("<TelerikDialog", StringComparison.Ordinal))
                offenders.Add(Path.GetFileName(file));
        }

        Assert.True(scanned > 0, "No .razor files were scanned — has the layout moved?");
        Assert.True(offenders.Count == 0,
            "TelerikDialog does not deliver input events to Blazor, so any field inside one is "
            + "dead and any Save button gated on a typed value can never enable. Use TelerikWindow "
            + "with the buttons inside WindowContent. Found in:\n  "
            + string.Join("\n  ", offenders));
    }
}
