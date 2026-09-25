using System.Security.Claims;
using System.Text.Json;
using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Data.WebApi.Services;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Reading one queued letter, and the record it leaves (item 245).
/// </summary>
/// <remarks>
/// <para><b>A letter is a copy of somebody else's business.</b> A booking confirmation names a
/// guest, a reset carries a working link, a pass carries a QR that opens a door. SuperAdmin may
/// look — somebody has to be able to answer "did that letter go, and what did it say" — so the
/// thing worth being certain about is not that looking is refused, but that looking is recorded.</para>
/// </remarks>
public sealed class AdminMailOutboxBodyTests
{
    private static readonly Guid ReaderId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Remembers what it was told, so a test can read the audit row.</summary>
    private sealed class SpyAudit : IAuditLogService
    {
        public List<(string Type, Guid Id, string Json, Guid User)> Reads { get; } = [];

        public Task LogCreateAsync(string t, Guid i, object e, Guid u, string s) => Task.CompletedTask;
        public Task LogUpdateAsync(string t, Guid i, object b, object a, Guid u, string s) => Task.CompletedTask;
        public Task LogDeleteAsync(string t, Guid i, object e, Guid u, string s) => Task.CompletedTask;

        public Task LogReadAsync(string entityType, Guid entityId, object what, Guid userId, string source)
        {
            // Serialized here as the real service does, so a payload that would throw in
            // production throws in the test rather than passing as an opaque object.
            Reads.Add((entityType, entityId, JsonSerializer.Serialize(what), userId));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingAudit : IAuditLogService
    {
        public Task LogCreateAsync(string t, Guid i, object e, Guid u, string s) => Task.CompletedTask;
        public Task LogUpdateAsync(string t, Guid i, object b, object a, Guid u, string s) => Task.CompletedTask;
        public Task LogDeleteAsync(string t, Guid i, object e, Guid u, string s) => Task.CompletedTask;
        public Task LogReadAsync(string t, Guid i, object w, Guid u, string s)
            => throw new InvalidOperationException("The audit sink is down.");
    }

    private static AdminMailDiagnosticsController Controller(SqliteTestDb sqlite, IAuditLogService audit)
    {
        var options = Options.Create(new SmtpOptions());
        var ctrl = new AdminMailDiagnosticsController(
            new SmtpEmailService(options, Options.Create(new SiteIdentity())),
            options, sqlite.Factory, audit,
            NullLogger<AdminMailDiagnosticsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, ReaderId.ToString())], "Bearer")),
                },
            },
        };
        return ctrl;
    }

    private static async Task<(SqliteTestDb Db, Guid LetterId)> SeedAsync(
        string? html = "<p>Hello</p>", DateTime? scrubbed = null)
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();

        var id = Guid.NewGuid();
        db.OutboxEmails.Add(new OutboxEmail
        {
            Id = id,
            To = "guest@example.test",
            Subject = "Your booking is confirmed",
            Kind = "event-booking-confirmed",
            HtmlBody = html,
            BodyScrubbedUtc = scrubbed,
            CreatedUtc = Now,
            NextAttemptUtc = Now,
        });
        await db.SaveChangesAsync();
        return (sqlite, id);
    }

    private static T? Value<T>(ActionResult<T> result) where T : class
        => (result.Result as OkObjectResult)?.Value as T ?? result.Value;

    [Fact]
    public async Task The_words_come_back()
    {
        var (sqlite, id) = await SeedAsync();
        await using var _ = sqlite;

        var body = Value(await Controller(sqlite, new SpyAudit()).Body(id, default));

        Assert.NotNull(body);
        Assert.Equal("<p>Hello</p>", body!.Html);
        Assert.Equal("guest@example.test", body.To);
    }

    /// <summary>The point of the whole endpoint's design.</summary>
    [Fact]
    public async Task Reading_a_letter_is_recorded_against_the_reader()
    {
        var (sqlite, id) = await SeedAsync();
        await using var _ = sqlite;
        var audit = new SpyAudit();

        await Controller(sqlite, audit).Body(id, default);

        var row = Assert.Single(audit.Reads);
        Assert.Equal(nameof(OutboxEmail), row.Type);
        Assert.Equal(id, row.Id);
        Assert.Equal(ReaderId, row.User);
        Assert.Contains("guest@example.test", row.Json);
    }

    /// <summary>
    /// A scrubbed letter says so, with its date.
    /// </summary>
    /// <remarks>
    /// Answering with an empty body would read as a letter that was sent blank, which is a
    /// different and much more alarming fact than "we no longer keep the words".
    /// </remarks>
    [Fact]
    public async Task A_scrubbed_letter_says_when_its_words_were_cleared()
    {
        var cleared = Now.AddDays(-1);
        var (sqlite, id) = await SeedAsync(html: null, scrubbed: cleared);
        await using var _ = sqlite;

        var body = Value(await Controller(sqlite, new SpyAudit()).Body(id, default));

        Assert.NotNull(body);
        Assert.Null(body!.Html);
        Assert.Equal(cleared, body.BodyScrubbedUtc);
    }

    [Fact]
    public async Task A_letter_that_is_not_there_is_not_found()
    {
        var (sqlite, _) = await SeedAsync();
        await using var __ = sqlite;

        var result = await Controller(sqlite, new SpyAudit()).Body(Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    /// <summary>
    /// An audit sink that is down must not stop somebody diagnosing the mail queue.
    /// </summary>
    /// <remarks>
    /// The opposite choice — refuse the read when it cannot be recorded — is defensible for money
    /// or for deletion. Here the screen exists precisely for the moment when things are broken,
    /// and a mail outage and a logging outage have the same cause often enough to matter.
    /// </remarks>
    [Fact]
    public async Task A_broken_audit_sink_does_not_break_the_screen()
    {
        var (sqlite, id) = await SeedAsync();
        await using var _ = sqlite;

        var body = Value(await Controller(sqlite, new ThrowingAudit()).Body(id, default));

        Assert.NotNull(body);
        Assert.Equal("<p>Hello</p>", body!.Html);
    }
}
