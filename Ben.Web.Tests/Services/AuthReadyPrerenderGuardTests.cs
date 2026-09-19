using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Guards against awaiting <c>AuthReady</c> without checking that the render is interactive.
/// </summary>
/// <remarks>
/// <para><c>AuthReady</c> is only ever signalled from the interactive circuit, by MainLayout's
/// first render. Awaiting it during the static prerender therefore never completes: the request
/// hangs until it times out, and the page returns nothing at all — no error page, no log line, just
/// a dead URL. It is a nasty failure precisely because nothing about it looks like a crash.</para>
///
/// <para><c>WaitUntilAuthReadyAsync(RendererInfo.IsInteractive)</c> exists to do this correctly. A
/// bare <c>await ...AuthReady</c> is the mistake, and it has been made more than once — hence a
/// test rather than a note.</para>
///
/// <para>Only <c>OnInitialized*</c> and <c>OnParametersSet*</c> are checked. <c>OnAfterRender*</c>
/// never runs during the static prerender at all, so a bare await there cannot hang anything —
/// <c>ClientRequestWizard</c> does exactly that, deliberately and correctly, and flagging it would
/// have made this test something people learn to ignore.</para>
/// </remarks>
public sealed class AuthReadyPrerenderGuardTests
{
    /// <summary>A bare await of the AuthReady task, not routed through the helper.</summary>
    private static readonly Regex BareAwait = new(
        @"await\s+[A-Za-z_][A-Za-z0-9_]*\.AuthReady\s*;",
        RegexOptions.Compiled);

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    [Fact]
    public void No_routable_page_awaits_AuthReady_without_the_interactive_check()
    {
        var root = RepoRoot();

        var offenders = new List<string>();
        var scanned = 0;

        var files = new[] { "Ben.Web.Website.Library", "Ben.Web.Website" }
            .Select(p => Path.Combine(root.FullName, p))
            .Where(Directory.Exists)
            .SelectMany(p => Directory.EnumerateFiles(p, "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            scanned++;

            // Only routable pages can be hit by a prerendered request; a child component renders
            // inside whatever its page already decided.
            if (!text.Contains("@page ")) continue;

            foreach (Match match in BareAwait.Matches(text))
            {
                var upToHere = text[..match.Index];

                // Which lifecycle method are we inside? The nearest preceding override wins.
                var lastOverride = upToHere.LastIndexOf("protected override", StringComparison.Ordinal);
                if (lastOverride < 0) continue;

                // The signature line only — not the whole body up to the match. ClientRequestWizard's
                // OnAfterRenderAsync has a comment mentioning OnInitializedAsync, which a span-wide
                // search reads as being inside it.
                var signatureEnd = upToHere.IndexOf('\n', lastOverride);
                var signature = signatureEnd < 0
                    ? upToHere[lastOverride..]
                    : upToHere[lastOverride..signatureEnd];
                var runsDuringPrerender =
                    signature.Contains("OnInitialized", StringComparison.Ordinal) ||
                    signature.Contains("OnParametersSet", StringComparison.Ordinal);

                if (!runsDuringPrerender) continue;

                // Allowed when interactivity was established earlier in the same method.
                var guarded = upToHere.LastIndexOf("RendererInfo.IsInteractive", StringComparison.Ordinal)
                              > lastOverride;

                if (!guarded) offenders.Add($"{Path.GetFileName(file)}: {match.Value.Trim()}");
            }
        }

        Assert.True(scanned > 0, "No .razor files were scanned — has the layout moved?");
        Assert.True(offenders.Count == 0,
            "These pages await AuthReady without checking RendererInfo.IsInteractive, which hangs "
            + "the static prerender and makes the URL return nothing at all. Use "
            + "UserState.WaitUntilAuthReadyAsync(RendererInfo.IsInteractive) instead:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The inverse hazard (item #103): reading auth state without ever waiting for it to resolve.
    /// </summary>
    /// <remarks>
    /// <para>Auth resolves asynchronously after the circuit connects. A component that reads
    /// <c>UserState.IsAuthenticated</c> and never follows that resolution renders the anonymous
    /// variant to a signed-in user on a hard navigation and stays that way — no crash, no log,
    /// just vote buttons that read "sign in to vote" for someone who is signed in. Six components
    /// shipped that way; the worst skipped loading the viewer's own votes entirely.</para>
    ///
    /// <para>A file passes by containing any of the recognised follow-the-answer tokens:
    /// <c>WaitUntilAuthReadyAsync</c>, a direct <c>.AuthReady</c> await (pages), a
    /// <c>StateChanged +=</c> subscription, or <c>EnsureStartedAsync</c> — NotificationState's
    /// startup, which performs the wait internally for the bell.</para>
    /// </remarks>
    [Fact]
    public void Every_reader_of_auth_state_follows_its_resolution()
    {
        var root = RepoRoot();
        var offenders = new List<string>();
        var scanned = 0;

        var files = new[] { "Ben.Web.Website.Library", "Ben.Web.Website" }
            .Select(p => Path.Combine(root.FullName, p))
            .Where(Directory.Exists)
            .SelectMany(p => Directory.EnumerateFiles(p, "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            scanned++;

            if (!text.Contains("UserState.IsAuthenticated", StringComparison.Ordinal)) continue;

            var follows =
                text.Contains("WaitUntilAuthReadyAsync", StringComparison.Ordinal) ||
                text.Contains(".AuthReady", StringComparison.Ordinal) ||
                text.Contains("StateChanged +=", StringComparison.Ordinal) ||
                text.Contains("EnsureStartedAsync", StringComparison.Ordinal);

            if (!follows) offenders.Add(Path.GetFileName(file));
        }

        Assert.True(scanned > 0, "No .razor files were scanned — has the layout moved?");
        Assert.True(offenders.Count == 0,
            "These files read UserState.IsAuthenticated but never follow auth resolution, so on a "
            + "hard navigation they can show a signed-in user the anonymous variant forever. Await "
            + "UserState.WaitUntilAuthReadyAsync(RendererInfo.IsInteractive) (then reload or "
            + "StateHasChanged), or subscribe to StateChanged:\n  "
            + string.Join("\n  ", offenders));
    }
}
