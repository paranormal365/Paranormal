using Ben.Data.Common;
using Ben.Data.Common.Interfaces;
using Ben.Data.Common.Mail;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A letter wears a written template without its mailer knowing templates exist (item 246).
/// </summary>
/// <remarks>
/// <para><b>Why this moved.</b> A letter used to gain a template by consulting
/// <see cref="MailComposer"/> itself, and exactly three of the thirty-five did — because doing it
/// meant editing each mailer, and each edit broke the tests that mock its sender. It happened
/// twice and the backlog predicted it would happen eleven more times.</para>
///
/// <para>So the composing happens once, in the outbox, where every letter already passes. A mailer
/// opts in by NAMING ITS KIND and, if it wants row tokens, by saying what it is holding. The
/// assertions below are about that: a plain letter with a kind comes out templated, and the words
/// the template was given come from the payload rather than from anything the mailer did.</para>
/// </remarks>
public sealed class EveryLetterCanCarryATemplateTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>A sender that records what it was handed and never touches a network.</summary>
    private sealed class Records : IEmailService
    {
        public bool IsConfigured => true;
        public Task SendAsync(string to, string subject, string html, CancellationToken ct = default)
            => SendAsync(new EmailMessage(to, subject, html), ct);
        public Task SendAsync(EmailMessage message, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static async Task<OutboxEmailService> BuildAsync(
        IDbContextFactory<BenDataContext> factory, string kind, string subject, string html)
    {
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.EmailTemplates.Add(new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Kind = kind,
                Subject = subject,
                BodyHtml = html,
                PublishedUtc = DateTime.UtcNow,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com" });

        return new OutboxEmailService(
            factory,
            // The relay is never reached: the outbox writes the row and a background pass sends it.
            sender: null!,
            site,
            new MailComposer(factory, new MemoryCache(new MemoryCacheOptions()), site,
                             NullLogger<MailComposer>.Instance),
            NullLogger<OutboxEmailService>.Instance);
    }

    private static async Task<OutboxEmail?> QueuedAsync(IDbContextFactory<BenDataContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.OutboxEmails.AsNoTracking().FirstOrDefaultAsync();
    }

    /// <summary>
    /// The mailer says only which letter this is, and the written words are what go out.
    /// </summary>
    [Fact]
    public async Task A_letter_that_names_its_kind_wears_the_written_template()
    {
        var factory = CreateFactory();
        var outbox = await BuildAsync(factory, MailKinds.ResetYourPassword.Key,
            "A word from {SiteName}", "<p>Written by hand.</p>");

        await outbox.SendAsync(new EmailMessage(
            "someone@example.test", "The built-in subject", "<p>The built-in words.</p>",
            Kind: MailKinds.ResetYourPassword.Key));

        var row = await QueuedAsync(factory);
        Assert.NotNull(row);
        Assert.Equal("A word from IsHaunted.com", row!.Subject);
        Assert.Contains("Written by hand.", row.HtmlBody);
        Assert.DoesNotContain("The built-in words.", row.HtmlBody);
    }

    /// <summary>
    /// Rows travel on the message, so a template can address them.
    /// </summary>
    [Fact]
    public async Task A_template_reads_the_rows_the_letter_is_carrying()
    {
        var factory = CreateFactory();
        var outbox = await BuildAsync(factory, MailKinds.ResetYourPassword.Key,
            "Hello {AppUsers.DisplayName}", "<p>Hello {AppUsers.DisplayName}.</p>");

        await outbox.SendAsync(new EmailMessage(
            "someone@example.test", "built-in", "<p>built-in</p>",
            Kind: MailKinds.ResetYourPassword.Key,
            Payload: new MailPayload(
                Tables: new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AppUsers"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["DisplayName"] = "Casey Hollow",
                    },
                })));

        var row = await QueuedAsync(factory);
        Assert.Equal("Hello Casey Hollow", row!.Subject);
        Assert.Contains("Hello Casey Hollow.", row.HtmlBody);
    }

    /// <summary>
    /// A letter with no declared kind is left exactly as it was.
    /// </summary>
    /// <remarks>
    /// The outbox still guesses a kind from the subject for grouping, and that guess must never
    /// reach a template: a template keyed to a guess stops applying the day somebody rewords a
    /// subject line, which is a worse failure than having no template at all.
    /// </remarks>
    [Fact]
    public async Task A_letter_with_no_kind_is_not_templated_by_a_guess()
    {
        var factory = CreateFactory();
        var outbox = await BuildAsync(factory, MailKinds.ResetYourPassword.Key,
            "Written subject", "<p>Written words.</p>");

        await outbox.SendAsync(new EmailMessage(
            "someone@example.test", "Reset your password", "<p>The built-in words.</p>"));

        var row = await QueuedAsync(factory);
        Assert.Equal("Reset your password", row!.Subject);
        Assert.Contains("The built-in words.", row.HtmlBody);
    }

    /// <summary>
    /// An unpublished template is not a template yet.
    /// </summary>
    [Fact]
    public async Task A_draft_nobody_published_does_not_go_out()
    {
        var factory = CreateFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.EmailTemplates.Add(new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Kind = MailKinds.ResetYourPassword.Key,
                Subject = "A draft",
                BodyHtml = "<p>Not finished.</p>",
                PublishedUtc = null,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com" });
        var outbox = new OutboxEmailService(
            factory, sender: null!, site,
            new MailComposer(factory, new MemoryCache(new MemoryCacheOptions()), site,
                             NullLogger<MailComposer>.Instance),
            NullLogger<OutboxEmailService>.Instance);

        await outbox.SendAsync(new EmailMessage(
            "someone@example.test", "The built-in subject", "<p>The built-in words.</p>",
            Kind: MailKinds.ResetYourPassword.Key));

        var row = await QueuedAsync(factory);
        Assert.Equal("The built-in subject", row!.Subject);
        Assert.Contains("The built-in words.", row.HtmlBody);
    }
}
