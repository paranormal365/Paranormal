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
