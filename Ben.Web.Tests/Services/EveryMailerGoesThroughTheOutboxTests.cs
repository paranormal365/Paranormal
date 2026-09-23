using System.Text.RegularExpressions;
using Ben.Web.Tests.Support;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Nothing sends mail except the sender job (item 239).
/// </summary>
/// <remarks>
/// <para>The outbox is only worth having if it is the only road. A service that takes
/// <c>SmtpEmailService</c> directly sends behind its back — no row, no retry, no record — and it
/// would do so silently, because the letter really does go the first time on a working machine.
/// The failure only shows up on the day the relay is down, which is exactly the day nobody can
/// find out what happened. That is the 2026-08-31 failure again, and this test is what stops it
/// coming back.</para>
///
/// <para>Three places may hold the raw sender, and each is named with its reason below.</para>
/// </remarks>
public sealed class EveryMailerGoesThroughTheOutboxTests
{
    /// <summary>Why each of these is allowed to reach past the outbox.</summary>
    private static readonly Dictionary<string, string> Allowed = new()
    {
        ["SmtpEmailService.cs"] =
            "is the sender.",
        ["OutboxEmailService.cs"] =
            "is the outbox, and holds the sender only to answer whether this machine could post at all.",
        ["MailSenderJob.cs"] =
            "is the one thing that posts what the outbox holds.",
        ["AdminMailDiagnosticsController.cs"] =
            "must send immediately and show the raw failure — a diagnostic that queues is not a diagnostic.",
        ["Program.cs"] =
            "registers them.",
        ["TestOutbox.cs"] =
            "builds the outbox for tests exactly as Program.cs does, which means building the "
          + "sender it wraps. It never posts: the outbox only queues, and no sender job runs in a test.",
    };

    [Fact]
    public void Only_the_sender_the_outbox_and_the_diagnostic_may_name_the_smtp_service()
    {
        var offenders = new List<string>();

        foreach (var path in RepoFiles.Paths("*.cs"))
        {
            var name = Path.GetFileName(path);
            if (Allowed.ContainsKey(name)) continue;
            if (name.EndsWith("Tests.cs", StringComparison.Ordinal)) continue;

            var source = WithoutComments(File.ReadAllText(path));
            if (Regex.IsMatch(source, @"\bSmtpEmailService\b"))
                offenders.Add(Path.GetRelativePath(RepoFiles.Root().FullName, path));
        }

        Assert.True(offenders.Count == 0,
            "These name SmtpEmailService directly and would send behind the outbox — no row, no "
          + "retry, no record of whether it went:\n  " + string.Join("\n  ", offenders)
          + "\nAsk for IEmailService instead, or add the file to Allowed with the reason it is one "
          + "of the three that may post directly.");
    }

    [Fact]
    public void The_interface_every_caller_asks_for_is_wired_to_the_outbox()
    {
        // The registration IS the feature: swapping it back would take every letter off the queue
        // in one line and break nothing that a test would otherwise notice.
        var program = File.ReadAllText(Path.Combine(RepoFiles.Root().FullName, "Ben.Data.WebApi", "Program.cs"));

        Assert.Matches(
            @"AddSingleton<Ben\.Data\.Common\.Interfaces\.IEmailService,\s*Ben\.Data\.WebApi\.Services\.OutboxEmailService>",
            program.Replace("\r", "").Replace("\n", " "));
        Assert.Contains("Scheduling.MailSenderJob>()", program);
    }

    [Fact]
    public void The_column_says_accepted_rather_than_sent()
    {
        // Naming it SentUtc would make every screen that reads it quietly claim the letter
        // arrived, which is the one thing nobody can know without bounce reports we do not
        // collect — and the exact claim that was wrong on 2026-08-31.
        var entity = WithoutComments(File.ReadAllText(Path.Combine(
            RepoFiles.Root().FullName, "Ben.Data.Source", "Entities", "BenDataModel.OutboxEmail.cs")));

        Assert.Contains("AcceptedBySmtpUtc", entity);
        Assert.DoesNotMatch(@"\bSentUtc\b", entity);
    }

    // ── item 239b step 3: a caller holding a context chooses its road ─────────

    /// <summary>
    /// Every method that holds a <c>BenDataContext</c> and still sends through the context-less
    /// <c>IEmailService.SendAsync</c> — directly, or through a helper in the same class — and why.
    /// </summary>
    /// <remarks>
    /// <para><b>The ratchet.</b> A letter queued through <c>IOutboxEmailQueue.EnqueueAsync</c> with
    /// the caller's context commits with the thing it is about, or neither does (item 239b). One
    /// sent through <c>SendAsync</c> is written on a connection of its own, after the fact — so a
    /// change can commit with its letter lost, or a letter can describe a change that rolled back.
    /// The backlog is explicit that not every letter should move: only where silence is expensive.
    /// So this list is today's callers, each with its reason, and it may only get shorter. A new
    /// caller fails until somebody writes down why it is fine, which is the whole point.</para>
    ///
    /// <para><b>Every entry below sends AFTER what it describes is saved</b> (checked 2026-09-23,
    /// call site by call site). So none can announce a change that rolled back; the failure each
    /// risks is a change that stands with its letter lost — logged as an Error by the outbox.</para>
    ///
    /// <para>Identity's own mail (<c>IEmailSender</c>) cannot take a context and is out of scope.</para>
    /// </remarks>
    private static readonly Dictionary<string, string> SendsBesideItsContext = new()
    {
        ["ClientStatusMailer.cs: SendToClientsAsync"] =
            "case-status and visit notices, after the change is saved; the client's case page shows "
          + "the same status and visit either way.",
        ["EventCreditExpiryJob.cs: WarnAsync"] =
            "a warning ahead of a credit's expiry (the bell is used only where there is no address); "
          + "losing it costs the holder the reminder, not the credit, which expires when it always would.",
        ["EventGuestMailer.cs: SendCalledOffAsync"] =
            "after the cancellation is saved; the guest's own page shows it called off. THE ONE MOST "
          + "WORTH MOVING — a guest who is not told turns up to nothing.",
        ["EventGuestMailer.cs: SendGoingAheadAsync"] =
            "after the go decision is saved; a lost one leaves a guest unsure, not misled.",
        ["EventGuestMailer.cs: SendHoldLapsedAsync"] =
            "after the hold has been released; the guest's booking page shows it lapsed.",
        ["EventGuestMailer.cs: SendHoldPlacedAsync"] =
            "after the hold is saved. WORTH MOVING — its own remarks say the deadline IS the letter, "
          + "and a guest who never learns it cannot act before the clock decides.",
        ["EventGuestMailer.cs: SendToSignUpsAsync"] =
            "session moved / cancelled / promoted notices, after the change is saved; the guest's "
          + "own page shows the session as it now is.",
        ["EventGuestMailer.cs: SendVenueWithdrewAsync"] =
            "after the withdrawal is saved; like the called-off letter, worth moving.",
        ["EventReminderJob.cs: RunAsync"] =
            "the night-before reminder of something the guest was told when they signed up; losing "
          + "it costs a nudge, not information.",
        ["HostedEventLifecycleJob.cs: TellTheOrganizersAsync"] =
            "mail beside a platform message that is always written; the message is the record.",
        ["HostedEventRetentionJob.cs: WarnAsync"] =
            "one of two warnings, a month and a week ahead of a clearing; losing one leaves the other.",
        ["MediaRetentionJob.cs: WarnAsync"] =
            "one of two notices, a week and a day ahead of a deletion; losing one leaves the other.",
        ["MyContactInfoController.cs: IssueValidationAsync"] =
            "the confirmation link is also returned to the page, and \"Send confirmation link\" "
          + "issues a fresh one — a lost letter is one click to replace.",
        ["TourGuestMailer.cs: SendAsync"] =
            "the tour welcome, after the seat is approved and saved; the reminder job resends the "
          + "walk the night before.",
        ["VenueClaimController.cs: SendCodeAsync"] =
            "the claim code, after the claim is saved; the claim page's resend issues a new code.",
    };

    [Fact]
    public void Every_caller_holding_a_context_that_sends_beside_it_is_named()
    {
        var root = RepoFiles.Root().FullName;
        var api = Path.Combine(root, "Ben.Data.WebApi") + Path.DirectorySeparatorChar;
        var found = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var path in RepoFiles.Paths("*.cs").Where(p => p.StartsWith(api, StringComparison.Ordinal)))
        {
            var source = NoCredentialsInLogsTests.WithoutComments(File.ReadAllText(path));
            var fields = Regex.Matches(source, @"\bIEmailService\??\s+(\w+)").Select(m => m.Groups[1].Value).ToHashSet();
            if (fields.Count == 0) continue;

            var sends = new Regex(@"\b(?:" + string.Join("|", fields.Select(Regex.Escape)) + @")\.SendAsync\(");
            var methods = Methods(source).ToList();
            var senders = methods.Where(m => sends.IsMatch(m.Body)).Select(m => m.Name).ToHashSet();

            foreach (var (name, parameters, body) in methods)
            {
                var holds = parameters.Contains("BenDataContext") || body.Contains("CreateDbContextAsync(")
                         || Regex.IsMatch(body, @"\bBenDataContext\b");
                if (!holds) continue;

                // Named by the method that SENDS, so a helper called from three places is one entry.
                if (senders.Contains(name)) found.Add($"{Path.GetFileName(path)}: {name}");
                foreach (var helper in senders.Where(h => h != name && Regex.IsMatch(body, $@"\b{h}\s*\(")))
                    found.Add($"{Path.GetFileName(path)}: {helper}");
            }
        }

        var unnamed = found.Where(f => !SendsBesideItsContext.ContainsKey(f)).ToList();
        Assert.True(unnamed.Count == 0,
            "These hold a BenDataContext and send through IEmailService.SendAsync, which writes the "
          + "letter on a connection of its own — so the change can commit with its letter lost. Queue "
          + "it with IOutboxEmailQueue.EnqueueAsync(db, …) and save once, or add it to "
          + "SendsBesideItsContext with the reason losing it is acceptable:\n  " + string.Join("\n  ", unnamed));

        var stale = SendsBesideItsContext.Keys.Where(k => !found.Contains(k)).ToList();
        Assert.True(stale.Count == 0,
            "These are no longer found — moved to the queue, renamed, or gone. Remove them, so the "
          + "list only ever gets shorter:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>Each method's name, parameter list and body, string literals respected.</summary>
    /// <remarks>
    /// A text scan, not a parser — the same bargain as the other guards here. It finds ordinary
    /// block-bodied methods, which is every sender in the API; an expression-bodied one that sends
    /// would have to hold its context in the arrow, and none does.
    /// </remarks>
    private static IEnumerable<(string Name, string Parameters, string Body)> Methods(string source)
    {
        var signature = new Regex(
            @"(?:public|private|internal|protected)(?:\s+(?:static|async|override|virtual|sealed|new))*"
          + @"\s+[\w<>\[\]?,.\s()]+?\s+(\w+)\s*(?:<[^>]*>)?\s*\(([^)]*(?:\([^)]*\)[^)]*)*)\)\s*(?:where[^{]*)?\{");

        foreach (Match m in signature.Matches(source))
        {
            var depth = 1;
            var i = m.Index + m.Length;
            for (; i < source.Length && depth > 0; i++)
            {
                var end = NoCredentialsInLogsTests.EndOfLiteral(source, i);
                if (end > i) { i = end - 1; continue; }
                if (source[i] == '{') depth++;
                else if (source[i] == '}') depth--;
            }
            yield return (m.Groups[1].Value, m.Groups[2].Value, source[(m.Index + m.Length)..Math.Min(i, source.Length)]);
        }
    }

    /// <summary>
    /// The code, without anything anybody wrote about the code.
    /// </summary>
    /// <remarks>
    /// Both facts above scan for a name, and this file's own remarks explain at length why that
    /// name must not appear — so without this, the guard's first act was to catch its own prose and
    /// to accuse <c>IdentityEmailSender</c>, which mentions the sender in a comment and correctly
    /// asks for the interface. A guard that cries wolf on comments is one somebody edits the
    /// comment to satisfy.
    /// </remarks>
    private static string WithoutComments(string source)
        => Regex.Replace(
               Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline),
               @"//[^\n]*", "");
}
