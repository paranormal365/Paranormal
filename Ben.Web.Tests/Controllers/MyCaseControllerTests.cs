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
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for MyCaseController — client-facing case dashboard, occurrences,
/// schedule proposals, co-clients, and investigation cancellation.
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
                ? new CaseTimelineEntryRecord { Id = e.Id, CaseId = e.CaseId, EntryType = e.EntryType, Title = e.Title, Body = e.Body, IsPublic = e.IsPublic, AuthorAppUserId = e.AuthorAppUserId, DateCreated = e.DateCreated }
                : new CaseTimelineEntryRecord { DateCreated = DateTime.UtcNow });
        return m.Object;
    }

    private static MyCaseController Build(IDbContextFactory<BenDataContext> factory, Guid userId, IAuditLogService? auditLog = null)
    {
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.CaseFilePath(It.IsAny<Guid>(), It.IsAny<string>())).Returns("fake/path");
        var ctrl = new MyCaseController(factory, CreateMapper(), storage.Object, new FileMetadataExtractorService(),
            auditLog ?? new Mock<IAuditLogService>().Object);
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
            new Mock<IFileStorageService>().Object, new FileMetadataExtractorService(), new Mock<IAuditLogService>().Object);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
        };
        return ctrl;
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
    }

    [Fact]
    public async Task GetMyCase_OtherUser_ReturnsNotFound()
    {
        var (factory, caseId, _, _) = await SeedClientCaseAsync();
        var ctrl = Build(factory, Guid.NewGuid());
        Assert.IsType<NotFoundResult>((await ctrl.GetMyCase(caseId, default)).Result);
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
        Assert.False(dto.IsPublic);
    }

    [Fact]
    public async Task LogOccurrence_OtherUser_ReturnsNotFound()
    {
        var (factory, caseId, _, _) = await SeedClientCaseAsync();
        var ctrl = Build(factory, Guid.NewGuid());
        Assert.IsType<NotFoundResult>((await ctrl.LogOccurrence(caseId, new LogOccurrenceRequest(null, "X", null), default)).Result);
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
}
