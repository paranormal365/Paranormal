using Ben.Data.Common;
using Ben.Data.Common.Mail;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Whether a letter uses the words somebody wrote, and what happens when it cannot (item 246).
/// </summary>
/// <remarks>
/// <b>Most of these are about the fallback.</b> A feature for editing letters must not become a
/// way to stop them going — which is the exact failure item 239 was built to end.
/// </remarks>
public sealed class MailComposerTests
{
    private static readonly DateTime Now = new(2026, 9, 20, 14, 5, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Nashville = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    private const string BuiltInSubject = "Reset your password";
    private const string BuiltInHtml = "<p>The letter the code writes.</p>";

    private static readonly Dictionary<string, IReadOnlyDictionary<string, object?>> Carrying = new()
    {
        ["AppUsers"] = new Dictionary<string, object?> { ["DisplayName"] = "Marguerite" },
    };

    private static MailComposer Composer(IDbContextFactory<BenDataContext> factory, IMemoryCache? cache = null)
        => new(factory, cache ?? new MemoryCache(new MemoryCacheOptions()),
               Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://ishaunted.test" }),
               NullLogger<MailComposer>.Instance);

    private static Task<(string Subject, string Html)> ComposeAsync(MailComposer composer)
        => composer.ComposeAsync(MailKinds.ResetYourPassword, Carrying, Nashville,
                                 BuiltInSubject, BuiltInHtml, Now, default);

    private static async Task AddTemplateAsync(
        SqliteTestDb sqlite, string? subject, string? html, bool published)
    {
        await using var db = await sqlite.NewContextAsync();
        db.EmailTemplates.Add(new EmailTemplate
        {
            Id = Guid.NewGuid(),
            Kind = MailKinds.ResetYourPassword.Key,
            Subject = published ? subject : null,
            BodyHtml = published ? html : null,
            PublishedUtc = published ? Now : null,
            DraftSubject = subject,
            DraftBodyHtml = html,
            DraftSavedUtc = Now,
            DateCreated = Now,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task With_no_template_the_built_in_letter_is_used()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();

        var (subject, html) = await ComposeAsync(Composer(sqlite.Factory));

        Assert.Equal(BuiltInSubject, subject);
        Assert.Equal(BuiltInHtml, html);
    }

    [Fact]
    public async Task A_published_template_replaces_the_letter_and_fills_its_tokens()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await AddTemplateAsync(sqlite,
            "Hello {AppUsers.DisplayName}",
            "<p>It is {FullDate} at {SiteName}.</p>", published: true);

        var (subject, html) = await ComposeAsync(Composer(sqlite.Factory));

        Assert.Equal("Hello Marguerite", subject);
        Assert.Equal("<p>It is September 20, 2026 at IsHaunted.com.</p>", html);
    }

    /// <summary>A draft is private until it is published.</summary>
    [Fact]
    public async Task An_unpublished_draft_changes_nothing_anybody_receives()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await AddTemplateAsync(sqlite, "Draft subject", "<p>Draft body.</p>", published: false);

        var (subject, html) = await ComposeAsync(Composer(sqlite.Factory));

        Assert.Equal(BuiltInSubject, subject);
        Assert.Equal(BuiltInHtml, html);
    }

    /// <summary>
    /// A template that renders to nothing is a mistake, not an instruction.
    /// </summary>
    /// <remarks>
    /// The likeliest way to get here is a body that is nothing but a token which resolved to
    /// nothing. Sending that is worse than sending the built-in letter, and worse silently.
    /// </remarks>
    [Fact]
    public async Task A_template_that_renders_empty_falls_back()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await AddTemplateAsync(sqlite, "{AppUsers.NoSuchColumn}", "{AppUsers.NoSuchColumn}", published: true);

        var (subject, html) = await ComposeAsync(Composer(sqlite.Factory));

        Assert.Equal(BuiltInSubject, subject);
        Assert.Equal(BuiltInHtml, html);
    }

    /// <summary>
    /// The one that decides whether this feature is safe to ship.
    /// </summary>
    [Fact]
    public async Task A_database_that_will_not_answer_still_sends_the_letter()
    {
        var (subject, html) = await ComposeAsync(Composer(new ThrowingFactory()));

        Assert.Equal(BuiltInSubject, subject);
        Assert.Equal(BuiltInHtml, html);
    }

    [Fact]
    public async Task Publishing_is_felt_once_the_kind_is_forgotten()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var composer = Composer(sqlite.Factory, cache);

        // Warms the cache with "there is no template", which is the common case and is cached too.
        Assert.Equal(BuiltInSubject, (await ComposeAsync(composer)).Subject);

        await AddTemplateAsync(sqlite, "Written by somebody", "<p>Theirs.</p>", published: true);
        Assert.Equal(BuiltInSubject, (await ComposeAsync(composer)).Subject);

        composer.Forget(MailKinds.ResetYourPassword.Key);
        Assert.Equal("Written by somebody", (await ComposeAsync(composer)).Subject);
    }

    private sealed class ThrowingFactory : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => throw new InvalidOperationException("No database today.");
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("No database today.");
    }
}
