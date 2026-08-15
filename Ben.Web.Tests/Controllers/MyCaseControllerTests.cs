using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for MyCaseController — client-facing case dashboard, occurrences,
/// schedule proposals, co-clients, sub-client invites, and investigation cancellation.
/// </summary>
public class MyCaseControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    // Non-pooled factory — controller uses FirstAsync with multi-level Includes
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SimpleFactory(opts);
    }

    private static IMapper CreateMapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<CaseTimelineEntryRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is CaseTimelineEntry e
                ? new CaseTimelineEntryRecord { Id = e.Id, CaseId = e.CaseId, EntryType = e.EntryType, Title = e.Title, Body = e.Body, Visibility = e.Visibility, AuthorAppUserId = e.AuthorAppUserId, DateCreated = e.DateCreated }
                : new CaseTimelineEntryRecord { DateCreated = DateTime.UtcNow });
        return m.Object;
    }

    private static MyCaseController Build(IDbContextFactory<BenDataContext> factory, Guid userId,
        IAuditLogService? auditLog = null, IEmailService? emailService = null)
    {
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.CaseFilePath(It.IsAny<Guid>(), It.IsAny<string>())).Returns("fake/path");
        var ctrl = new MyCaseController(factory, CreateMapper(), storage.Object, new FileMetadataExtractorService(),
            auditLog ?? new Mock<IAuditLogService>().Object,
            emailService ?? CreateUnconfiguredEmailService(), new ConfigurationBuilder().Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MyCaseController>.Instance);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"))
            }
        };
        return ctrl;
    }

    private static MyCaseController BuildAnonymous(IDbContextFactory<BenDataContext> factory)
    {
        var ctrl = new MyCaseController(factory, CreateMapper(),
            new Mock<IFileStorageService>().Object, new FileMetadataExtractorService(), new Mock<IAuditLogService>().Object,
            CreateUnconfiguredEmailService(), new ConfigurationBuilder().Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MyCaseController>.Instance);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
        };
        return ctrl;
    }

    private static IEmailService CreateUnconfiguredEmailService()
    {
        var mock = new Mock<IEmailService>();
        mock.Setup(e => e.IsConfigured).Returns(false);
        return mock.Object;
    }

    /// <summary>Seeds org, client user, client request, and accepted case.</summary>
    private static async Task<(IDbContextFactory<BenDataContext>, Guid caseId, Guid clientId, Guid orgId)> SeedClientCaseAsync()
    {
        var factory  = CreateFactory();
        var clientId = Guid.NewGuid();
        var orgId    = Guid.NewGuid();
        var caseId   = Guid.NewGuid();
        var adminId  = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = clientId, UserName = "client@t.com", NormalizedUserName = "CLIENT@T.COM", Email = "client@t.com", NormalizedEmail = "CLIENT@T.COM", DateCreated = DateTime.UtcNow });
        db.Users.Add(new AppUser { Id = adminId,  UserName = "admin@t.com",  NormalizedUserName = "ADMIN@T.COM",  Email = "admin@t.com",  NormalizedEmail = "ADMIN@T.COM",  DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = orgId, Name = "Test Org", UrlName = "test", DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });
        var clientReq = new ClientRequest { Id = Guid.NewGuid(), AppUserId = clientId, City = "Nashville", State = "TN", ZipCode = "37201", Country = "US", StreetAddress1 = "1 Main", Description = "Desc", Status = ClientRequestStatus.Assigned, DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId };
        db.ClientRequests.Add(clientReq);
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId, ClientRequestId = clientReq.Id,
            Title = "Test Case", CaseYear = DateTime.UtcNow.Year, OrgCaseNumber = 1,
            Status = CaseStatus.Accepted,
            StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
            DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
        });
        await db.SaveChangesAsync();
        return (factory, caseId, clientId, orgId);
    }

    // ── GetMyCases ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyCases_Unauthenticated_ReturnsUnauthorized()
    {
        var factory = CreateFactory();
        var ctrl = BuildAnonymous(factory);
        Assert.IsType<UnauthorizedResult>((await ctrl.GetMyCases(default)).Result);
    }

    [Fact]
    public async Task GetMyCases_ReturnsOnlyClientsCases()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var ctrl = Build(factory, clientId);
        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetMyCases(default)).Result);
        var list = Assert.IsAssignableFrom<IEnumerable<ClientCaseListItem>>(ok.Value);
        Assert.Single(list);
        Assert.Equal(caseId, list.First().CaseId);
    }

    [Fact]
    public async Task GetMyCases_OtherUser_ReturnsEmpty()
    {
        var (factory, _, _, _) = await SeedClientCaseAsync();
        var ctrl = Build(factory, Guid.NewGuid());
        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetMyCases(default)).Result);
        Assert.Empty((IEnumerable<ClientCaseListItem>)ok.Value!);
    }

    // ── GetMyCase ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyCase_Client_ReturnsDetail()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var ctrl = Build(factory, clientId);
        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetMyCase(caseId, default)).Result);
        var dto  = Assert.IsType<ClientCaseDetail>(ok.Value);
        Assert.Equal(caseId, dto.CaseId);
        Assert.True(dto.IsPrimaryClient);
    }

    [Fact]
    public async Task GetMyCase_CoClient_IsPrimaryClientFalse()
    {
        // Regression: MyCaseDetail.razor's Shared Access card previously inferred "am I primary"
        // from whether GetCoClients happened not to throw — but the generic HTTP client returns an
        // empty list on 403 instead of throwing, so co-clients incorrectly saw the primary-only
        // admin controls. IsPrimaryClient is now a real, server-computed field instead.
        var (factory, caseId, _, _) = await SeedClientCaseAsync();
        var coClientId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser { Id = coClientId, UserName = "co@t.com", NormalizedUserName = "CO@T.COM", Email = "co@t.com", NormalizedEmail = "CO@T.COM", DateCreated = DateTime.UtcNow });
            db.CaseClientAccesses.Add(new CaseClientAccess
            {
                Id = Guid.NewGuid(), CaseId = caseId, AppUserId = coClientId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = coClientId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl = Build(factory, coClientId);
        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetMyCase(caseId, default)).Result);
        Assert.False(Assert.IsType<ClientCaseDetail>(ok.Value).IsPrimaryClient);
    }

    [Fact]
    public async Task GetMyCase_OtherUser_ReturnsNotFound()
    {
        var (factory, caseId, _, _) = await SeedClientCaseAsync();
        var ctrl = Build(factory, Guid.NewGuid());
        Assert.IsType<NotFoundResult>((await ctrl.GetMyCase(caseId, default)).Result);
    }

    // ── Co-client access to case list/detail (regression — previously primary-client-only) ──────
    // A secondary co-client (via CaseClientAccess, from either the old AddCoClient flow or a
    // sub-client invite) must see the case in GetMyCases and be able to open GetMyCase — before
    // this fix, both endpoints checked only ClientRequest.AppUserId, so a co-client's grant did
    // nothing for browsing even though it worked for individual occurrence actions.

    [Fact]
    public async Task GetMyCases_IncludesCasesWhereUserIsCoClient()
    {
        var (factory, caseId, _, _) = await SeedClientCaseAsync();
        var coClientId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser { Id = coClientId, UserName = "co@t.com", NormalizedUserName = "CO@T.COM", Email = "co@t.com", NormalizedEmail = "CO@T.COM", DateCreated = DateTime.UtcNow });
            db.CaseClientAccesses.Add(new CaseClientAccess
            {
                Id = Guid.NewGuid(), CaseId = caseId, AppUserId = coClientId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = coClientId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl = Build(factory, coClientId);
        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetMyCases(default)).Result);
        var list = Assert.IsAssignableFrom<IEnumerable<ClientCaseListItem>>(ok.Value).ToList();
        Assert.Single(list);
        Assert.Equal(caseId, list[0].CaseId);
    }

    [Fact]
    public async Task GetMyCase_CoClient_ReturnsDetail()
    {
        var (factory, caseId, _, _) = await SeedClientCaseAsync();
        var coClientId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser { Id = coClientId, UserName = "co@t.com", NormalizedUserName = "CO@T.COM", Email = "co@t.com", NormalizedEmail = "CO@T.COM", DateCreated = DateTime.UtcNow });
            db.CaseClientAccesses.Add(new CaseClientAccess
            {
                Id = Guid.NewGuid(), CaseId = caseId, AppUserId = coClientId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = coClientId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl = Build(factory, coClientId);
        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetMyCase(caseId, default)).Result);
        Assert.Equal(caseId, Assert.IsType<ClientCaseDetail>(ok.Value).CaseId);
    }

    // ── LogOccurrence ─────────────────────────────────────────────────────────

    [Fact]
    public async Task LogOccurrence_CreatesClientReportEntry()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var ctrl   = Build(factory, clientId);
        var result = await ctrl.LogOccurrence(caseId, new LogOccurrenceRequest(DateTime.UtcNow, "Weird noise", "Loud banging"), default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<CaseTimelineEntryRecord>(ok.Value);
        Assert.Equal(CaseTimelineEntryType.ClientReport, dto.EntryType);
        Assert.Equal("Weird noise", dto.Title);
        // A client's own report starts internal — visible to them as its author, not shared on.
        Assert.Equal(CaseTimelineVisibility.OrgOnly, dto.Visibility);
    }

    [Fact]
    public async Task LogOccurrence_OtherUser_ReturnsNotFound()
    {
        var (factory, caseId, _, _) = await SeedClientCaseAsync();
        var ctrl = Build(factory, Guid.NewGuid());
        Assert.IsType<NotFoundResult>((await ctrl.LogOccurrence(caseId, new LogOccurrenceRequest(null, "X", null), default)).Result);
    }

    [Fact]
    public async Task LogOccurrence_CoClient_CreatesEntry()
    {
        // Regression: LogOccurrence previously checked only ClientRequest.AppUserId (primary
        // client), while its own IsCaseClient helper (used elsewhere in this controller) already
        // supported co-clients — a real co-client got NotFound trying to log an occurrence.
        var (factory, caseId, _, _) = await SeedClientCaseAsync();
        var coClientId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser { Id = coClientId, UserName = "co@t.com", NormalizedUserName = "CO@T.COM", Email = "co@t.com", NormalizedEmail = "CO@T.COM", DateCreated = DateTime.UtcNow });
            db.CaseClientAccesses.Add(new CaseClientAccess
            {
                Id = Guid.NewGuid(), CaseId = caseId, AppUserId = coClientId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = coClientId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl = Build(factory, coClientId);
        var result = await ctrl.LogOccurrence(caseId, new LogOccurrenceRequest(null, "Co-client report", null), default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Co-client report", Assert.IsType<CaseTimelineEntryRecord>(ok.Value).Title);
    }

    // ── UpdateOccurrence ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateOccurrence_UpdatesEntry()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var ctrl    = Build(factory, clientId);
        var entry   = (CaseTimelineEntryRecord)((OkObjectResult)(await ctrl.LogOccurrence(caseId, new LogOccurrenceRequest(null, "Original", "Body"), default)).Result!).Value!;

        var result  = await ctrl.UpdateOccurrence(caseId, entry.Id, new LogOccurrenceRequest(null, "Updated", "New body"), default);
        var ok      = Assert.IsType<OkObjectResult>(result.Result);
        var dto     = Assert.IsType<CaseTimelineEntryRecord>(ok.Value);
        Assert.Equal("Updated", dto.Title);
    }

    [Fact]
    public async Task UpdateOccurrence_OtherUser_ReturnsNotFound()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var client  = Build(factory, clientId);
        var entry   = (CaseTimelineEntryRecord)((OkObjectResult)(await client.LogOccurrence(caseId, new LogOccurrenceRequest(null, "X", null), default)).Result!).Value!;

        var other   = Build(factory, Guid.NewGuid());
        Assert.IsType<NotFoundResult>((await other.UpdateOccurrence(caseId, entry.Id, new LogOccurrenceRequest(null, "Hack", null), default)).Result);
    }

    [Fact]
    public async Task UpdateOccurrence_CoClient_UpdatesOwnEntry()
    {
        // Regression: entry.Case.ClientRequest?.AppUserId != userId rejected any caller who
        // wasn't the primary client — even though the query filter already guaranteed the
        // caller was the entry's own author (AuthorAppUserId == userId), so a co-client editing
        // an occurrence they themselves logged was wrongly Forbidden.
        var (factory, caseId, _, _) = await SeedClientCaseAsync();
        var coClientId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser { Id = coClientId, UserName = "co@t.com", NormalizedUserName = "CO@T.COM", Email = "co@t.com", NormalizedEmail = "CO@T.COM", DateCreated = DateTime.UtcNow });
            db.CaseClientAccesses.Add(new CaseClientAccess
            {
                Id = Guid.NewGuid(), CaseId = caseId, AppUserId = coClientId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = coClientId,
            });
            await db.SaveChangesAsync();
        }
        var ctrl  = Build(factory, coClientId);
        var entry = (CaseTimelineEntryRecord)((OkObjectResult)(await ctrl.LogOccurrence(caseId, new LogOccurrenceRequest(null, "Original", "Body"), default)).Result!).Value!;

        var result = await ctrl.UpdateOccurrence(caseId, entry.Id, new LogOccurrenceRequest(null, "Updated", "New body"), default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Updated", Assert.IsType<CaseTimelineEntryRecord>(ok.Value).Title);
    }

    // ── DeleteOccurrence ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteOccurrence_DeletesEntry()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var ctrl  = Build(factory, clientId);
        var entry = (CaseTimelineEntryRecord)((OkObjectResult)(await ctrl.LogOccurrence(caseId, new LogOccurrenceRequest(null, "X", null), default)).Result!).Value!;

        Assert.IsType<NoContentResult>(await ctrl.DeleteOccurrence(caseId, entry.Id, default));

        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.CaseTimelineEntries.AnyAsync(e => e.Id == entry.Id));
    }

    [Fact]
    public async Task DeleteOccurrence_CoClient_DeletesOwnEntry()
    {
        var (factory, caseId, _, _) = await SeedClientCaseAsync();
        var coClientId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser { Id = coClientId, UserName = "co@t.com", NormalizedUserName = "CO@T.COM", Email = "co@t.com", NormalizedEmail = "CO@T.COM", DateCreated = DateTime.UtcNow });
            db.CaseClientAccesses.Add(new CaseClientAccess
            {
                Id = Guid.NewGuid(), CaseId = caseId, AppUserId = coClientId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = coClientId,
            });
            await db.SaveChangesAsync();
        }
        var ctrl  = Build(factory, coClientId);
        var entry = (CaseTimelineEntryRecord)((OkObjectResult)(await ctrl.LogOccurrence(caseId, new LogOccurrenceRequest(null, "X", null), default)).Result!).Value!;

        Assert.IsType<NoContentResult>(await ctrl.DeleteOccurrence(caseId, entry.Id, default));

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.False(await verifyDb.CaseTimelineEntries.AnyAsync(e => e.Id == entry.Id));
    }

    // ── Schedule proposals ────────────────────────────────────────────────────

    [Fact]
    public async Task GetScheduleProposals_ReturnsEmpty_WhenNone()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var ctrl = Build(factory, clientId);
        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetScheduleProposals(caseId, default)).Result);
        Assert.Empty((IEnumerable<ScheduleProposalDto>)ok.Value!);
    }

    [Fact]
    public async Task AcceptProposal_CreatesInvestigation()
    {
        var (factory, caseId, clientId, orgId) = await SeedClientCaseAsync();
        var slotId     = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var scheduledTime = DateTime.UtcNow.AddDays(10);

        await using var db = await factory.CreateDbContextAsync();
        var proposal = new InvestigationScheduleProposal
        {
            Id = proposalId, CaseId = caseId,
            Status = ScheduleProposalStatus.Pending,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
        };
        db.InvestigationScheduleProposals.Add(proposal);
        db.ScheduleProposalSlots.Add(new ScheduleProposalSlot
        {
            Id = slotId, ProposalId = proposalId,
            StartDateTime = scheduledTime, SortOrder = 1,
        });
        await db.SaveChangesAsync();

        var ctrl   = Build(factory, clientId);
        var result = await ctrl.AcceptProposal(caseId, proposalId, new AcceptProposalRequest(slotId), default);

        Assert.IsType<OkObjectResult>(result.Result);

        await using var db2 = await factory.CreateDbContextAsync();
        Assert.True(await db2.Investigations.AnyAsync(i => i.CaseId == caseId));
        var updated = await db2.InvestigationScheduleProposals.FindAsync(proposalId);
        Assert.Equal(ScheduleProposalStatus.AcceptedByClient, updated!.Status);
    }

    [Fact]
    public async Task CounterProposal_SetsCounteredStatus()
    {
        var (factory, caseId, clientId, orgId) = await SeedClientCaseAsync();
        var proposalId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.InvestigationScheduleProposals.Add(new InvestigationScheduleProposal
        {
            Id = proposalId, CaseId = caseId,
            Status = ScheduleProposalStatus.Pending,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
        });
        await db.SaveChangesAsync();

        var ctrl   = Build(factory, clientId);
        var result = await ctrl.CounterProposal(caseId, proposalId, new CounterProposalRequest(DateTime.UtcNow.AddDays(14), "Weekends only"), default);

        Assert.IsType<OkObjectResult>(result.Result);
        await using var db2 = await factory.CreateDbContextAsync();
        var updated = await db2.InvestigationScheduleProposals.FindAsync(proposalId);
        Assert.Equal(ScheduleProposalStatus.Countered, updated!.Status);
    }

    [Fact]
    public async Task DeclineProposal_SetsDeclinedStatus()
    {
        var (factory, caseId, clientId, orgId) = await SeedClientCaseAsync();
        var proposalId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.InvestigationScheduleProposals.Add(new InvestigationScheduleProposal
        {
            Id = proposalId, CaseId = caseId,
            Status = ScheduleProposalStatus.Pending,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
        });
        await db.SaveChangesAsync();

        var ctrl   = Build(factory, clientId);
        var result = await ctrl.DeclineProposal(caseId, proposalId, new DeclineProposalRequest("Not ready"), default);

        Assert.IsType<OkObjectResult>(result.Result);
        await using var db2 = await factory.CreateDbContextAsync();
        var updated = await db2.InvestigationScheduleProposals.FindAsync(proposalId);
        Assert.Equal(ScheduleProposalStatus.Declined, updated!.Status);
    }

    // ── Co-clients ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCoClients_ReturnsEmptyList()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var ctrl = Build(factory, clientId);
        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetCoClients(caseId, default)).Result);
        Assert.Empty((IEnumerable<CoClientItem>)ok.Value!);
    }

    [Fact]
    public async Task AddCoClient_AddsAccessByEmail()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var coClientId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = coClientId, UserName = "co@t.com", NormalizedUserName = "CO@T.COM", Email = "co@t.com", NormalizedEmail = "CO@T.COM", DateCreated = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var ctrl   = Build(factory, clientId);
        var result = await ctrl.AddCoClient(caseId, new AddCoClientRequest("co@t.com"), default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<CoClientItem>(ok.Value);
        Assert.Equal(coClientId, dto.AppUserId);
    }

    [Fact]
    public async Task AddCoClient_DuplicateEmail_ReturnsConflict()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var coClientId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = coClientId, UserName = "co@t.com", NormalizedUserName = "CO@T.COM", Email = "co@t.com", NormalizedEmail = "CO@T.COM", DateCreated = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var ctrl = Build(factory, clientId);
        await ctrl.AddCoClient(caseId, new AddCoClientRequest("co@t.com"), default);
        var result = await ctrl.AddCoClient(caseId, new AddCoClientRequest("co@t.com"), default);
        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task AddCoClient_UnknownEmail_ReturnsBadRequest()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var ctrl   = Build(factory, clientId);
        var result = await ctrl.AddCoClient(caseId, new AddCoClientRequest("nobody@t.com"), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task RemoveCoClient_DeletesAccess()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var coClientId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = coClientId, UserName = "co@t.com", NormalizedUserName = "CO@T.COM", Email = "co@t.com", NormalizedEmail = "CO@T.COM", DateCreated = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var ctrl     = Build(factory, clientId);
        var dto      = (CoClientItem)((OkObjectResult)(await ctrl.AddCoClient(caseId, new AddCoClientRequest("co@t.com"), default)).Result!).Value!;
        var result   = await ctrl.RemoveCoClient(caseId, dto.AccessId, default);

        Assert.IsType<NoContentResult>(result);
        await using var db2 = await factory.CreateDbContextAsync();
        Assert.False(await db2.CaseClientAccesses.AnyAsync(a => a.Id == dto.AccessId));
    }

    // ── Sub-client invites (item #4) ──────────────────────────────────────────────

    [Fact]
    public async Task InviteCoClient_ExistingAccount_LinksImmediatelyWithoutCreatingInvite()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var coClientId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser { Id = coClientId, UserName = "co@t.com", NormalizedUserName = "CO@T.COM", Email = "co@t.com", NormalizedEmail = "CO@T.COM", DateCreated = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, clientId);
        var result = await ctrl.InviteCoClient(caseId, new InviteCoClientRequest("co@t.com"), default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<InviteCoClientResult>(ok.Value);
        Assert.True(dto.LinkedExistingAccount);
        Assert.Equal(coClientId, dto.CoClient?.AppUserId);
        Assert.Null(dto.Invite);
        Assert.False(dto.EmailSent);

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.True(await verifyDb.CaseClientAccesses.AnyAsync(a => a.CaseId == caseId && a.AppUserId == coClientId));
        Assert.False(await verifyDb.CaseClientInvites.AnyAsync(i => i.CaseId == caseId));
    }

    [Fact]
    public async Task InviteCoClient_NoAccount_CreatesFourteenDayInvite()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var ctrl   = Build(factory, clientId);
        var before = DateTime.UtcNow;

        var result = await ctrl.InviteCoClient(caseId, new InviteCoClientRequest("newperson@t.com"), default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<InviteCoClientResult>(ok.Value);
        Assert.False(dto.LinkedExistingAccount);
        Assert.Null(dto.CoClient);
        Assert.NotNull(dto.Invite);
        Assert.Equal("newperson@t.com", dto.Invite!.Email);
        Assert.False(string.IsNullOrWhiteSpace(dto.Invite.Token));
        Assert.InRange(dto.Invite.DateExpires, before.AddDays(14).AddMinutes(-1), before.AddDays(14).AddMinutes(1));
        Assert.False(dto.EmailSent); // unconfigured IEmailService in tests
    }

    [Fact]
    public async Task InviteCoClient_EmailConfigured_SendsAndReportsSent()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var email = new Mock<IEmailService>();
        email.Setup(e => e.IsConfigured).Returns(true);
        email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        var ctrl = Build(factory, clientId, emailService: email.Object);

        var result = await ctrl.InviteCoClient(caseId, new InviteCoClientRequest("newperson@t.com"), default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<InviteCoClientResult>(ok.Value);
        Assert.True(dto.EmailSent);
        email.Verify(e => e.SendAsync("newperson@t.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InviteCoClient_SendFailure_StillSucceedsButReportsNotSent()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var email = new Mock<IEmailService>();
        email.Setup(e => e.IsConfigured).Returns(true);
        email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("SMTP unreachable"));
        var ctrl = Build(factory, clientId, emailService: email.Object);

        var result = await ctrl.InviteCoClient(caseId, new InviteCoClientRequest("newperson@t.com"), default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<InviteCoClientResult>(ok.Value);
        Assert.NotNull(dto.Invite); // invite still created despite the send failure
        Assert.False(dto.EmailSent);
    }

    [Fact]
    public async Task InviteCoClient_ReInvitingSameEmail_RevokesThePriorPendingInvite()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var ctrl = Build(factory, clientId);

        var first  = (InviteCoClientResult)((OkObjectResult)(await ctrl.InviteCoClient(caseId, new InviteCoClientRequest("newperson@t.com"), default)).Result!).Value!;
        var second = (InviteCoClientResult)((OkObjectResult)(await ctrl.InviteCoClient(caseId, new InviteCoClientRequest("newperson@t.com"), default)).Result!).Value!;

        Assert.NotEqual(first.Invite!.Token, second.Invite!.Token);

        await using var db = await factory.CreateDbContextAsync();
        var firstRow = await db.CaseClientInvites.FirstAsync(i => i.Id == first.Invite.Id);
        Assert.NotNull(firstRow.DateRevoked);
        var secondRow = await db.CaseClientInvites.FirstAsync(i => i.Id == second.Invite.Id);
        Assert.Null(secondRow.DateRevoked);
    }

    [Fact]
    public async Task InviteCoClient_NonPrimaryClient_ReturnsForbid()
    {
        var (factory, caseId, _, _) = await SeedClientCaseAsync();
        var ctrl   = Build(factory, Guid.NewGuid());
        var result = await ctrl.InviteCoClient(caseId, new InviteCoClientRequest("newperson@t.com"), default);
        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetInvites_ExcludesAcceptedRevokedAndExpired()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CaseClientInvites.Add(new CaseClientInvite // pending
            {
                Id = Guid.NewGuid(), CaseId = caseId, Email = "pending@t.com", Token = "tok-pending",
                DateExpires = DateTime.UtcNow.AddDays(14), DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
            });
            db.CaseClientInvites.Add(new CaseClientInvite // accepted
            {
                Id = Guid.NewGuid(), CaseId = caseId, Email = "used@t.com", Token = "tok-used",
                DateExpires = DateTime.UtcNow.AddDays(14), DateAccepted = DateTime.UtcNow,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
            });
            db.CaseClientInvites.Add(new CaseClientInvite // revoked
            {
                Id = Guid.NewGuid(), CaseId = caseId, Email = "revoked@t.com", Token = "tok-revoked",
                DateExpires = DateTime.UtcNow.AddDays(14), DateRevoked = DateTime.UtcNow,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
            });
            db.CaseClientInvites.Add(new CaseClientInvite // expired
            {
                Id = Guid.NewGuid(), CaseId = caseId, Email = "expired@t.com", Token = "tok-expired",
                DateExpires = DateTime.UtcNow.AddDays(-1), DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl = Build(factory, clientId);
        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetInvites(caseId, default)).Result);
        var invites = Assert.IsAssignableFrom<IEnumerable<CaseClientInviteRecord>>(ok.Value).ToList();

        Assert.Single(invites);
        Assert.Equal("pending@t.com", invites[0].Email);
    }

    [Fact]
    public async Task RevokeInvite_SetsDateRevoked()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var ctrl   = Build(factory, clientId);
        var invite = (InviteCoClientResult)((OkObjectResult)(await ctrl.InviteCoClient(caseId, new InviteCoClientRequest("newperson@t.com"), default)).Result!).Value!;

        var result = await ctrl.RevokeInvite(caseId, invite.Invite!.Id, default);
        Assert.IsType<NoContentResult>(result);

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.CaseClientInvites.FirstAsync(i => i.Id == invite.Invite.Id);
        Assert.NotNull(row.DateRevoked);
    }

    [Fact]
    public async Task RevokeInvite_NonPrimaryClient_ReturnsForbid()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var ctrl   = Build(factory, clientId);
        var invite = (InviteCoClientResult)((OkObjectResult)(await ctrl.InviteCoClient(caseId, new InviteCoClientRequest("newperson@t.com"), default)).Result!).Value!;

        var otherCtrl = Build(factory, Guid.NewGuid());
        var result = await otherCtrl.RevokeInvite(caseId, invite.Invite!.Id, default);
        Assert.IsType<ForbidResult>(result);
    }

    // ── Related people (basic-info, no account) ─────────────────────────────────

    [Fact]
    public async Task AddRelatedPerson_ReturnsRecord_AndPersists()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var ctrl = Build(factory, clientId);

        var result = await ctrl.AddRelatedPerson(caseId,
            new AddRelatedPersonRequest("Jane Doe", 34, "Spouse", true, "Sleeps poorly upstairs."), default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<CaseRelatedPersonRecord>(ok.Value);
        Assert.Equal("Jane Doe", record.Name);
        Assert.True(record.LivesAtProperty);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.CaseRelatedPeople.AnyAsync(p => p.Id == record.Id && p.CaseId == caseId));
    }

    [Fact]
    public async Task AddRelatedPerson_MissingName_ReturnsBadRequest()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var ctrl = Build(factory, clientId);

        var result = await ctrl.AddRelatedPerson(caseId,
            new AddRelatedPersonRequest("  ", null, null, false, null), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task AddRelatedPerson_NotPrimaryClient_ReturnsForbid()
    {
        var (factory, caseId, _, _) = await SeedClientCaseAsync();
        var ctrl = Build(factory, Guid.NewGuid());

        var result = await ctrl.AddRelatedPerson(caseId,
            new AddRelatedPersonRequest("Jane Doe", null, null, false, null), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetRelatedPeople_ReturnsAddedPeople()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var ctrl = Build(factory, clientId);
        await ctrl.AddRelatedPerson(caseId, new AddRelatedPersonRequest("Jane Doe", null, null, false, null), default);

        var result = await ctrl.GetRelatedPeople(caseId, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<CaseRelatedPersonRecord>>(ok.Value);
        Assert.Single(list);
    }

    [Fact]
    public async Task RemoveRelatedPerson_DeletesRow()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var ctrl = Build(factory, clientId);
        var added = (CaseRelatedPersonRecord)((OkObjectResult)(await ctrl.AddRelatedPerson(
            caseId, new AddRelatedPersonRequest("Jane Doe", null, null, false, null), default)).Result!).Value!;

        var result = await ctrl.RemoveRelatedPerson(caseId, added.Id, default);

        Assert.IsType<NoContentResult>(result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.CaseRelatedPeople.AnyAsync(p => p.Id == added.Id));
    }

    [Fact]
    public async Task RemoveRelatedPerson_NotPrimaryClient_ReturnsForbid()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var owner = Build(factory, clientId);
        var added = (CaseRelatedPersonRecord)((OkObjectResult)(await owner.AddRelatedPerson(
            caseId, new AddRelatedPersonRequest("Jane Doe", null, null, false, null), default)).Result!).Value!;

        var other = Build(factory, Guid.NewGuid());
        var result = await other.RemoveRelatedPerson(caseId, added.Id, default);

        Assert.IsType<ForbidResult>(result);
    }

    // ── Audit logging on client case-log writes ──────────────────────────────────

    [Fact]
    public async Task LogOccurrence_WritesAuditCreateEntry()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var auditLog = new Mock<IAuditLogService>();
        var ctrl = Build(factory, clientId, auditLog.Object);

        await ctrl.LogOccurrence(caseId, new LogOccurrenceRequest(DateTime.UtcNow, "Title", "Body"), default);

        auditLog.Verify(a => a.LogCreateAsync(
            nameof(CaseTimelineEntry), It.IsAny<Guid>(), It.IsAny<object>(), clientId, AppSources.WebApi, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AddRelatedPerson_WritesAuditCreateEntry()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        var auditLog = new Mock<IAuditLogService>();
        var ctrl = Build(factory, clientId, auditLog.Object);

        await ctrl.AddRelatedPerson(caseId, new AddRelatedPersonRequest("Jane Doe", null, null, false, null), default);

        auditLog.Verify(a => a.LogCreateAsync(
            nameof(Ben.Data.Source.Entities.CaseRelatedPerson), It.IsAny<Guid>(), It.IsAny<object>(), clientId, AppSources.WebApi, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Timeline visibility tiers (C2) ────────────────────────────────────────

    /// <summary>Adds an org-authored timeline entry at the given visibility.</summary>
    private static async Task<Guid> SeedOrgEntryAsync(
        IDbContextFactory<BenDataContext> factory, Guid caseId, CaseTimelineVisibility visibility,
        string title, CaseTimelineEntryType type = CaseTimelineEntryType.InvestigatorNote)
    {
        var entryId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.CaseTimelineEntries.Add(new CaseTimelineEntry
        {
            Id = entryId, CaseId = caseId,
            AuthorAppUserId = Guid.NewGuid(),          // an investigator, not the client
            EntryType = type, Title = title, Body = "<p>body</p>",
            Visibility = visibility,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        return entryId;
    }

    private static async Task<IReadOnlyList<ClientCaseOccurrence>> ClientTimelineAsync(
        IDbContextFactory<BenDataContext> factory, Guid clientId, Guid caseId)
    {
        var result = await Build(factory, clientId).GetMyCase(caseId, default);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<ClientCaseDetail>(ok.Value).Occurrences;
    }

    [Fact]
    public async Task ClientTimeline_HidesOrgOnlyEntries()
    {
        // The default for working notes. A client must never see these.
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        await SeedOrgEntryAsync(factory, caseId, CaseTimelineVisibility.OrgOnly, "Internal theory");

        var timeline = await ClientTimelineAsync(factory, clientId, caseId);

        Assert.DoesNotContain(timeline, o => o.Title == "Internal theory");
    }

    [Fact]
    public async Task ClientTimeline_ShowsEntriesSharedWithTheClient()
    {
        // The new capability. Before this tier existed, telling a client anything meant publishing
        // it to the whole internet.
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        await SeedOrgEntryAsync(factory, caseId, CaseTimelineVisibility.Client, "For your eyes");

        var timeline = await ClientTimelineAsync(factory, clientId, caseId);

        var entry = Assert.Single(timeline, o => o.Title == "For your eyes");
        Assert.True(entry.FromInvestigators);
    }

    [Fact]
    public async Task ClientTimeline_ShowsPublicEntriesToo()
    {
        // Cumulative: anything the public can see, the client can see.
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        await SeedOrgEntryAsync(factory, caseId, CaseTimelineVisibility.Public, "Published finding");

        var timeline = await ClientTimelineAsync(factory, clientId, caseId);

        Assert.Contains(timeline, o => o.Title == "Published finding");
    }

    [Fact]
    public async Task ClientTimeline_StillShowsTheClientsOwnReports_EvenThoughTheyAreOrgOnly()
    {
        // A client's own report is created OrgOnly — "not shared onward", not "hidden from its
        // author". If this ever regressed, clients would lose sight of their own submissions.
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        await Build(factory, clientId).LogOccurrence(caseId,
            new LogOccurrenceRequest(DateTime.UtcNow, "My own report", "<p>I heard it.</p>"), default);

        var timeline = await ClientTimelineAsync(factory, clientId, caseId);

        var mine = Assert.Single(timeline, o => o.Title == "My own report");
        Assert.False(mine.FromInvestigators);
    }

    [Fact]
    public async Task ClientTimeline_SeparatesTheirOwnEntriesFromTheOrgs()
    {
        var (factory, caseId, clientId, _) = await SeedClientCaseAsync();
        await Build(factory, clientId).LogOccurrence(caseId,
            new LogOccurrenceRequest(DateTime.UtcNow, "Mine", "<p>x</p>"), default);
        await SeedOrgEntryAsync(factory, caseId, CaseTimelineVisibility.Client, "Theirs");

        var timeline = await ClientTimelineAsync(factory, clientId, caseId);

        Assert.False(Assert.Single(timeline, o => o.Title == "Mine").FromInvestigators);
        Assert.True(Assert.Single(timeline, o => o.Title == "Theirs").FromInvestigators);
    }
}
