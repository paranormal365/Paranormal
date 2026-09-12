using Ben.Web.Tests.Support;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// One mark for "this was proved", and the rule that stops it becoming a dead end (item 237).
/// </summary>
/// <remarks>
/// <para>These are source-scanning guards rather than render tests, because what is worth
/// protecting is not the markup — it is that <b>nobody invents a fifth tick</b>. Four screens were
/// each drawing their own, and four marks meaning the same thing while looking different teach a
/// reader that a tick means whatever this screen felt like.</para>
///
/// <para>They read the tree they were built from via <see cref="RepoFiles"/>, which excludes
/// worktrees nested in that root by prefix. A guard that scans nothing must shout, so each one
/// asserts it found files before judging them.</para>
/// </remarks>
public sealed class BenProvenTests
{
    private const string Component = "BenProven.razor";

    /// <summary>The screens that state a proved fact, and must state it the same way.</summary>
    private static readonly string[] ScreensThatProve =
    [
        "MyEmailsCard.razor",     // each contact address
        "MyProfile.razor",        // the account address, and Apple
        "TwoFactorPanel.razor",   // two-factor on or off
        "AdminUsers.razor",       // the same facts, seen by an administrator
    ];

    [Fact]
    public void The_component_exists_and_is_in_the_shared_kit()
    {
        // In Kit, not beside one of its callers: the next screen that needs it should find it
        // where every other shared control lives, rather than importing from a feature folder.
        var found = RepoFiles.Paths("*.razor")
            .Where(p => Path.GetFileName(p) == Component)
            .ToList();

        Assert.Single(found);
        Assert.Contains($"Kit{Path.DirectorySeparatorChar}{Component}", found[0]);
    }

    [Fact]
    public void Every_screen_that_states_a_proved_fact_uses_it()
    {
        var razor = RepoFiles.Paths("*.razor");
        Assert.NotEmpty(razor);

        var missing = new List<string>();
        foreach (var name in ScreensThatProve)
        {
            var path = razor.FirstOrDefault(p => Path.GetFileName(p) == name);
            Assert.NotNull(path);   // the screen itself went missing, which is a different problem

            if (!File.ReadAllText(path!).Contains("<BenProven"))
                missing.Add(name);
        }

        Assert.True(missing.Count == 0,
            "these state a fact as proved without the shared mark, so the site now has more than "
            + "one vocabulary for the same thing:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void No_screen_hand_rolls_a_confirmed_pill_any_more()
    {
        // The specific shapes that were there before, and would come back the moment somebody
        // copies an old row. Narrow on purpose: this must catch a regression, not every green pill
        // on the site.
        string[] banned =
        [
            "badge bg-success ms-1\">Confirmed",
            "badge bg-warning text-dark ms-1\">Not confirmed",
        ];

        var offenders = new List<string>();
        foreach (var path in RepoFiles.Paths("*.razor"))
        {
            if (Path.GetFileName(path) == Component) continue;
            var text = File.ReadAllText(path);
            if (banned.Any(text.Contains))
                offenders.Add(Path.GetFileName(path));
        }

        Assert.True(offenders.Count == 0,
            "a hand-rolled confirmed pill is back; use BenProven:\n  " + string.Join("\n  ", offenders));
    }

    // ── The rule that makes it safe to use ───────────────────────────────────

    [Fact]
    public void An_unproved_fact_with_no_path_draws_nothing()
    {
        // The contract, read off the component itself: the unproved branch is guarded by Unproved
        // being non-empty. Without that guard somebody would ship "Not verified" against a fact
        // nobody can change — which reads as a fault the person caused and has no way through.
        var component = RepoFiles.Paths("*.razor").Single(p => Path.GetFileName(p) == Component);
        var text = File.ReadAllText(component);

        Assert.Contains("else if (!string.IsNullOrWhiteSpace(Unproved))", text);
    }

    [Fact]
    public void Phone_numbers_are_not_marked_because_nothing_can_ever_verify_one()
    {
        // UserPhone.IsValidated exists and is written false on create; no endpoint and no screen
        // can set it. Marking a phone "not verified" would be exactly the dead end the component
        // is built to prevent, so the phones card must stay unmarked until a flow exists — and
        // when one does, this test is the reminder to come back.
        var phones = RepoFiles.Paths("*.razor")
            .FirstOrDefault(p => Path.GetFileName(p) == "MyPhonesCard.razor");
        Assert.NotNull(phones);

        Assert.DoesNotContain("<BenProven", File.ReadAllText(phones!));
    }
}
