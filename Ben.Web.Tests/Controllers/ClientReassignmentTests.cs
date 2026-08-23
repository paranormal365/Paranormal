using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Item 84's second half: a client moves their paused case, and their per-category consent is
/// enforced at the moment of acceptance.
/// </summary>
/// <remarks>
/// The consent mechanics are the part worth pinning hard, because both directions fail silently:
/// history that should have been withheld quietly appears in the new group's timeline, or
/// investigations the client DID share quietly vanish. Nothing here is deleted in any branch —
/// withheld history is re-scoped to the client, withheld investigations detach and remain the
/// original group's flat records.
/// </remarks>
public sealed class ClientReassignmentTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static IDbContextFactory<BenDataContext> Factory() =>
        new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record World(
        IDbContextFactory<BenDataContext> F, Guid FromOrg, Guid ToOrg, Guid CaseId,
        Guid ClientId, Guid ToAdminId, Guid LogId, Guid OrgOnlyEntry, Guid PublicEntry, Guid InvestigationId);

    private static async Task<World> SeedAsync(bool shareHistory, bool shareInvestigations)
    {
        var f = Factory();
        var fromOrg = Guid.NewGuid(); var toOrg = Guid.NewGuid();
        var caseId = Guid.NewGuid(); var clientId = Guid.NewGuid(); var toAdmin = Guid.NewGuid();
        var logId = Guid.NewGuid(); var orgOnly = Guid.NewGuid(); var pub = Guid.NewGuid(); var inv = Guid.NewGuid();

        await using var db = await f.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = clientId, UserName = "cl@t.com", Email = "cl@t.com", DateCreated = DateTime.UtcNow });
        db.Users.Add(new AppUser { Id = toAdmin, UserName = "ta@t.com", Email = "ta@t.com", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = fromOrg, Name = "Old Group", UrlName = "old", DateCreated = DateTime.UtcNow, CreatedByAppUserId = toAdmin });
        db.Organizations.Add(new Organization { Id = toOrg, Name = "New Group", UrlName = "new", DateCreated = DateTime.UtcNow, CreatedByAppUserId = toAdmin });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = toOrg, AppUserId = toAdmin,
            Role = OrganizationMemberRole.Owner, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = toAdmin,
        });
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = fromOrg, Title = "Moving case",
            Status = CaseStatus.Paused, StatusBeforePause = CaseStatus.Active,
            CaseYear = 2026, OrgCaseNumber = 3,
            StreetAddress1 = "1 Main", City = "N", State = "TN", ZipCode = "1", Country = "US",
            DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = toAdmin,
        });
        db.CaseTimelineEntries.Add(new CaseTimelineEntry
        {
            Id = orgOnly, CaseId = caseId, AuthorAppUserId = toAdmin,
            EntryType = CaseTimelineEntryType.InvestigatorNote, Title = "Working note",
            Visibility = CaseTimelineVisibility.OrgOnly,
            EventDateTime = DateTime.UtcNow, DateCreated = DateTime.UtcNow,
        });
        db.CaseTimelineEntries.Add(new CaseTimelineEntry
        {
            Id = pub, CaseId = caseId, AuthorAppUserId = toAdmin,
            EntryType = CaseTimelineEntryType.InvestigatorNote, Title = "Published finding",
            Visibility = CaseTimelineVisibility.Public,
            EventDateTime = DateTime.UtcNow, DateCreated = DateTime.UtcNow,
        });
        db.Investigations.Add(new Investigation
        {
            Id = inv, OrganizationId = fromOrg, CaseId = caseId,
            Title = "Night visit", Status = InvestigationStatus.Completed,
            ScheduledDateTime = DateTime.UtcNow.AddDays(-30),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = toAdmin,
        });
        db.CaseTransferLogs.Add(new CaseTransferLog
        {
            Id = logId, CaseId = caseId,
            FromOrganizationId = fromOrg, ToOrganizationId = toOrg,
            ProposedByAppUserId = clientId, ProposedByClient = true,
            ShareHistory = shareHistory, ShareInvestigations = shareInvestigations,
            Status = CaseTransferStatus.Pending, DateProposed = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return new World(f, fromOrg, toOrg, caseId, clientId, toAdmin, logId, orgOnly, pub, inv);
    }

    private static CaseTransferController Controller(IDbContextFactory<BenDataContext> f, Guid userId)
    {
        var ctrl = new CaseTransferController(f, MapperStub.Create(),
            new Ben.Data.WebApi.Services.PlatformMessageService(f), new Ben.Service.RepositoryService.Services.OrganizationSecurityService(f))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"))
                }
            }
        };
        return ctrl;
    }

    private static class MapperStub
    {
        public static AutoMapper.IMapper Create()
        {
            var m = new Moq.Mock<AutoMapper.IMapper>();
            m.Setup(x => x.Map<CaseTransferLogRecord>(Moq.It.IsAny<object>()))
                .Returns(new CaseTransferLogRecord());
            return m.Object;
        }
    }

    private static Task Accept(World w) =>
        Controller(w.F, w.ToAdminId).Respond(w.ToOrg, w.CaseId, w.LogId,
            new RespondTransferRequest(true, null), default);

    // ── consent enforcement, all four combinations ────────────────────────────

    [Fact]
    public async Task Withholding_history_rescopes_it_to_the_client_and_leaves_public_entries_public()
    {
        var w = await SeedAsync(shareHistory: false, shareInvestigations: true);

        await Accept(w);

        await using var db = await w.F.CreateDbContextAsync();
        Assert.Equal(CaseTimelineVisibility.ClientOnly,
            (await db.CaseTimelineEntries.SingleAsync(e => e.Id == w.OrgOnlyEntry)).Visibility);
        // Already published to the world; withholding from the new group cannot mean less.
        Assert.Equal(CaseTimelineVisibility.Public,
            (await db.CaseTimelineEntries.SingleAsync(e => e.Id == w.PublicEntry)).Visibility);
    }

    [Fact]
    public async Task Sharing_history_leaves_every_entry_exactly_as_it_was()
    {
        var w = await SeedAsync(shareHistory: true, shareInvestigations: true);

        await Accept(w);

        await using var db = await w.F.CreateDbContextAsync();
        Assert.Equal(CaseTimelineVisibility.OrgOnly,
            (await db.CaseTimelineEntries.SingleAsync(e => e.Id == w.OrgOnlyEntry)).Visibility);
    }

    /// <summary>
    /// Withheld investigations detach and survive — the original group's flat records, not gone.
    /// </summary>
    [Fact]
    public async Task Withholding_investigations_detaches_them_without_deleting_anything()
    {
        var w = await SeedAsync(shareHistory: true, shareInvestigations: false);

        await Accept(w);

        await using var db = await w.F.CreateDbContextAsync();
        var inv = await db.Investigations.SingleAsync(i => i.Id == w.InvestigationId);
        Assert.Null(inv.CaseId);                          // no longer travels with the case
        Assert.Equal(w.FromOrg, inv.OrganizationId);      // still the original group's record
    }

    /// <summary>
    /// Shared investigations stay attached AND stay the original group's — dual visibility with
    /// no copy, which is what dual ownership means mechanically.
    /// </summary>
    [Fact]
    public async Task Sharing_investigations_keeps_them_attached_and_owned_by_the_original_group()
    {
        var w = await SeedAsync(shareHistory: true, shareInvestigations: true);

        await Accept(w);

        await using var db = await w.F.CreateDbContextAsync();
        var inv = await db.Investigations.SingleAsync(i => i.Id == w.InvestigationId);
        Assert.Equal(w.CaseId, inv.CaseId);
        Assert.Equal(w.FromOrg, inv.OrganizationId);
    }

    // ── the move itself ───────────────────────────────────────────────────────

    [Fact]
    public async Task Acceptance_moves_the_case_and_clears_the_pause_marker()
    {
        var w = await SeedAsync(shareHistory: true, shareInvestigations: true);

        await Accept(w);

        await using var db = await w.F.CreateDbContextAsync();
        var c = await db.Cases.SingleAsync(x => x.Id == w.CaseId);
        Assert.Equal(w.ToOrg, c.OrganizationId);
        Assert.Equal(CaseStatus.Accepted, c.Status);
        Assert.Null(c.StatusBeforePause);                 // a fresh start, not a suspended old one
    }

    [Fact]
    public async Task Acceptance_answers_the_client_with_a_message()
    {
        var w = await SeedAsync(shareHistory: true, shareInvestigations: true);

        await Accept(w);

        await using var db = await w.F.CreateDbContextAsync();
        Assert.Equal(1, await db.UserMessageTos.CountAsync(t => t.ToAppUserId == w.ClientId));
    }

    [Fact]
    public async Task The_incoming_inbox_shows_the_pending_move_with_its_consent_flags()
    {
        var w = await SeedAsync(shareHistory: false, shareInvestigations: true);

        var result = await Controller(w.F, w.ToAdminId).GetIncoming(w.ToOrg, default);

        var rows = Assert.IsAssignableFrom<IEnumerable<CaseTransferController.IncomingTransferRecord>>(
            ((OkObjectResult)result.Result!).Value).ToList();
        var row = Assert.Single(rows);
        Assert.True(row.ProposedByClient);
        Assert.False(row.ShareHistory);
        Assert.True(row.ShareInvestigations);
        Assert.Equal("Old Group", row.FromOrganizationName);
    }
}
