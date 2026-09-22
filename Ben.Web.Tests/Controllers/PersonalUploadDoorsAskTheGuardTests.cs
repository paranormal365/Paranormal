using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Every door that writes bytes under a person asks whether they have room first.
/// </summary>
/// <remarks>
/// <para><b>Counting is the easy half.</b> <see cref="Ben.Data.WebApi.Services.Billing.AccountStorageGuard"/>
/// measures what an account is holding, and since 2026-09-22 it does so with one rule that covers
/// every personal door automatically. None of that refuses anything. A cap is only a cap where
/// somebody asks it, and the guard's own remarks have said so since it was written: "any third
/// kind adds a term HERE and a call at its own door; neither alone is enough."</para>
///
/// <para>When this was first measured there were fifteen places writing under
/// <c>UserFilePath</c> and four calls to the guard, across three features. The other eleven doors
/// would take a file of any size from an account already over its limit.</para>
///
/// <para><b>Why a file-level scan and not something cleverer.</b> It is coarse — a file with two
/// doors passes on one call — and it is the version that keeps working. The alternative is
/// parsing C# to find the enclosing method, which fails quietly the first time somebody extracts
/// a helper. The allowlist below is where the coarseness is paid for: anything that genuinely
/// should not ask is named with its reason, and the list is meant to shrink.</para>
/// </remarks>
public sealed class PersonalUploadDoorsAskTheGuardTests
{
    /// <summary>
    /// Files that write under a person and do not ask, with the reason each is allowed to.
    /// </summary>
    /// <remarks>
    /// Every entry is a hole. One that stops being true is worse than no entry, so each says what
    /// would make it wrong.
    /// </remarks>
    private static readonly Dictionary<string, string> NeedNotAsk = new()
    {
        ["UploadFileAudioClipController.cs"] =
            "Writes a clip cut FROM a file the account is already being charged for, and the clip "
          + "is smaller than its source by construction. Wrong the day clipping can produce "
          + "something larger, or can run on a file the caller does not own.",

        ["UploadFileAudioEditController.cs"] =
            "Same shape as the clip door: an edit of bytes already counted. Wrong if editing ever "
          + "grows a file materially.",

        ["PublicHostedEventRoomController.cs"] =
            "An attendee's photo of a room at somebody else's event. It is stored under the "
          + "attendee, but the event's host is who invited it, and refusing a guest at a paid "
          + "group's event because of their own free allowance would be a strange rule. Revisit "
          + "with the event-files limit, which is declared and also enforced nowhere.",
    };

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void Every_door_that_stores_under_a_person_asks_about_room_first()
    {
        var controllers = Path.Combine(RepoRoot().FullName, "Ben.Data.WebApi", "Controllers");
        var silent = new List<string>();

        foreach (var file in Directory.EnumerateFiles(controllers, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            if (!Regex.IsMatch(text, @"\bUserFilePath\s*\(")) continue;
            if (text.Contains("WhyCannotStoreAsync", StringComparison.Ordinal)) continue;

            var name = Path.GetFileName(file);
            if (NeedNotAsk.ContainsKey(name)) continue;

            silent.Add(name);
        }

        Assert.True(silent.Count == 0,
            "These write bytes under a person and never ask AccountStorageGuard whether the "
          + "account has room, so the cap does not exist at those doors:\n  "
          + string.Join("\n  ", silent.Distinct().OrderBy(x => x))
          + "\n\nAdd the check, or add the file to NeedNotAsk with the reason it is exempt.");
    }

    /// <summary>The allowlist names files that exist and still write under a person.</summary>
    /// <remarks>
    /// A stale exemption is the failure mode this whole file is about: the entry goes on reading
    /// as a considered decision long after the door it describes has moved or started asking.
    /// </remarks>
    [Fact]
    public void The_allowlist_has_no_stale_entries()
    {
        var controllers = Path.Combine(RepoRoot().FullName, "Ben.Data.WebApi", "Controllers");
        var stale = new List<string>();

        foreach (var (name, _) in NeedNotAsk)
        {
            var match = Directory.EnumerateFiles(controllers, name, SearchOption.AllDirectories)
                .FirstOrDefault();

            if (match is null) { stale.Add($"{name} — no such controller any more"); continue; }

            var text = File.ReadAllText(match);
            if (!Regex.IsMatch(text, @"\bUserFilePath\s*\("))
                stale.Add($"{name} — no longer writes under a person");
            else if (text.Contains("WhyCannotStoreAsync", StringComparison.Ordinal))
                stale.Add($"{name} — asks the guard now, so the exemption is spent");
        }

        Assert.True(stale.Count == 0,
            "Exemptions that have stopped being true:\n  " + string.Join("\n  ", stale));
    }
}
