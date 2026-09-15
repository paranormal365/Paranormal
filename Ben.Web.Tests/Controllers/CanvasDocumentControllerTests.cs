using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The case canvas API: who may read and write a board, the revision contract, and what a save
/// does to the markup inside it.
/// </summary>
/// <remarks>
/// <para><b>Real permission services, not mocks.</b> A mocked <c>MayAsync</c> would prove the
/// controller asks; it would not prove a read grant is refused a write. So these seed a group whose
/// every member holds Case.Read through the bridge role, give the editor a direct grant of the four
/// case actions, and give one member everything except Delete.</para>
///
/// <para><b>Why the race test uses SQLite.</b> The revision check has two halves: the compare in
/// C# answers a save that is plainly stale, and the concurrency token answers two saves that both
/// passed the compare. Only a relational provider runs the second half as a real
/// <c>UPDATE ... WHERE Revision = @loaded</c>.</para>
/// </remarks>
public sealed class CanvasDocumentControllerTests
{
    // ── fixture ───────────────────────────────────────────────────────────────

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory,
        Guid OrgId, Guid CaseId,
        Guid EditorId, Guid ViewerId, Guid NoDeleteId, Guid OutsiderId);

    private static IDbContextFactory<BenDataContext> InMemory()
        => new Microsoft.EntityFrameworkCore.Infrastructure.PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<World> SeedAsync(IDbContextFactory<BenDataContext>? factory = null)
    {
        factory ??= InMemory();
        var orgId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var editorId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        var noDeleteId = Guid.NewGuid();
        var outsiderId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            foreach (var (id, name) in new[]
                     {
                         (ownerId, "Owner Person"), (editorId, "Erin Editor"), (viewerId, "Val Viewer"),
                         (noDeleteId, "Nora Nodelete"), (outsiderId, "Otto Outsider"),
                     })
            {
                db.Users.Add(new AppUser
                {
                    Id = id, UserName = $"{id:N}@t.com", NormalizedUserName = $"{id:N}@T.COM",
                    Email = $"{id:N}@t.com", NormalizedEmail = $"{id:N}@T.COM",
                    DisplayName = name, DateCreated = DateTime.UtcNow,
                });
            }
            db.Organizations.Add(new Organization
            {
                Id = orgId, Name = "Night Watch", UrlName = $"nw-{orgId:N}",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            foreach (var member in new[] { editorId, viewerId, noDeleteId })
            {
                db.OrganizationUserMemberships.Add(new OrganizationUserMembership
                {
                    Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = member,
                    Role = OrganizationMemberRole.Member, IsActive = true,
                    DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
                });
            }
            db.Cases.Add(new Case
            {
                Id = caseId, OrganizationId = orgId, Title = "Henderson",
                CaseYear = 2026, OrgCaseNumber = 1,
                StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        // Every member reads (the bridge); the editor does everything; one member cannot delete.
        await TestSeeds.BridgeAsync(factory, orgId, TestSeeds.ReadOnly);
        await TestSeeds.GrantAsync(factory, orgId, editorId, OrganizationSecurityTable.Case, TestSeeds.CaseWork);
        await TestSeeds.GrantAsync(factory, orgId, noDeleteId, OrganizationSecurityTable.Case,
            OrganizationSecurityAction.Read | OrganizationSecurityAction.Create | OrganizationSecurityAction.Update);

        return new World(factory, orgId, caseId, editorId, viewerId, noDeleteId, outsiderId);
    }

    private static IMapper Mapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<CanvasDocumentRecord>(It.IsAny<object>()))
            .Returns<object>(o =>
            {
                var d = (CanvasDocument)o;
                return new CanvasDocumentRecord
                {
                    Id = d.Id, CaseId = d.CaseId, OrganizationId = d.Case?.OrganizationId,
                    Name = d.Name, DocumentJson = d.DocumentJson, Revision = d.Revision,
                    PublishedUploadFileId = d.PublishedUploadFileId, PublishedAtUtc = d.PublishedAtUtc,
                    DateCreated = d.DateCreated, DateUpdated = d.DateUpdated,
                    CreatedByAppUserId = d.CreatedByAppUserId,
                };
            });
        m.Setup(x => x.Map<CanvasDocumentSummaryRecord>(It.IsAny<object>()))
            .Returns<object>(o =>
            {
                var d = (CanvasDocument)o;
                return new CanvasDocumentSummaryRecord
                {
                    Id = d.Id, CaseId = d.CaseId, OrganizationId = d.Case?.OrganizationId,
                    Name = d.Name, Revision = d.Revision, DateCreated = d.DateCreated,
                    DateUpdated = d.DateUpdated, CreatedByAppUserId = d.CreatedByAppUserId,
                };
            });
        return m.Object;
    }

    private sealed record Built(CanvasDocumentController Controller, Mock<IAuditLogService> Audit, Mock<IMediaIngestService> Ingest, Mock<IFileStorageService> Storage);

    private static Built Build(IDbContextFactory<BenDataContext> factory, Guid? userId, string? ifMatch = null, bool superAdmin = false)
    {
        var audit = new Mock<IAuditLogService>();
        var ingest = new Mock<IMediaIngestService>();
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.CaseFilePath(It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns<Guid, string>((c, n) => $"cases/{c}/{n}");

        var ctrl = new CanvasDocumentController(
            factory, Mapper(), storage.Object, ingest.Object,
            new SubscriptionLimitGuard(factory),
            new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory),
            audit.Object, new CmsMarkupSanitizer());

        var claims = new List<Claim>();
        if (userId is { } id) claims.Add(new Claim(ClaimTypes.NameIdentifier, id.ToString()));
        if (superAdmin) claims.Add(new Claim(ClaimTypes.Role, RoleNames.SuperAdmin));
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, userId is null ? null : "Bearer")),
        };
        if (ifMatch is not null) http.Request.Headers.IfMatch = ifMatch;
        ctrl.ControllerContext = new ControllerContext { HttpContext = http };
        return new Built(ctrl, audit, ingest, storage);
    }

    private static JsonElement Board(string title = "Henderson board", string html = "<p>Knocking at 2am</p>")
        => JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            title,
            revision = 99,           // informational: the route and If-Match are the contract
            nodes = new object[]
            {
                new { id = Guid.NewGuid(), type = "message", x = 0, y = 0, width = 200, height = 100,
                      data = new { kind = "message", html, author = "Erin", timestampUtc = "2026-09-14T00:00:00Z" } },
                new { id = Guid.NewGuid(), type = "text", x = 300, y = 0, width = 200, height = 100,
                      data = new { kind = "text", text = "<b>not markup, just text</b>" } },
            },
            edges = Array.Empty<object>(),
            groups = Array.Empty<object>(),
        })).RootElement.Clone();

    private static async Task<CanvasDocumentRecord> CreateOnCaseAsync(World w, Guid? asUser = null, string title = "Henderson board")
    {
        var result = await Build(w.Factory, asUser ?? w.EditorId).Controller.Create(w.CaseId, Board(title), default);
        return (CanvasDocumentRecord)Assert.IsType<CreatedAtActionResult>(result.Result).Value!;
    }

    // ── the door ──────────────────────────────────────────────────────────────

    [Fact]
    public void The_controller_requires_sign_in_and_is_gated_on_the_canvas_flag()
    {
        Assert.NotNull(typeof(CanvasDocumentController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal(SiteSettingKeys.FeatureCanvasEditor,
            Support.FeatureGateProbe.KeyOf(Support.FeatureGateProbe.GateOn<CanvasDocumentController>()));
    }

    /// <summary>R1: a signed-in person on a site where nobody has written the flag's row gets 404.</summary>
    [Fact]
    public async Task A_signed_in_caller_gets_404_while_the_flag_has_never_been_set()
    {
        var (result, ran) = await Support.FeatureGateProbe.RunAsync(
            Support.FeatureGateProbe.GateOn<CanvasDocumentController>(),
            await Support.FeatureGateProbe.SettingsAsync());

        Assert.False(ran, "the canvas API answered a signed-in caller with the flag unset; it defaults off");
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        var w = await SeedAsync();
        var anon = Build(w.Factory, userId: null, ifMatch: "\"1\"").Controller;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => anon.GetAll(w.CaseId, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => anon.GetById(Guid.NewGuid(), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => anon.Create(w.CaseId, Board(), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => anon.Update(Guid.NewGuid(), Board(), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => anon.Delete(Guid.NewGuid(), default));
    }

    // ── reading ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_by_case_returns_every_members_document_not_only_mine()
    {
        var w = await SeedAsync();
        await CreateOnCaseAsync(w, w.EditorId, "Erin's board");
        await CreateOnCaseAsync(w, w.NoDeleteId, "Nora's board");

        var result = await Build(w.Factory, w.ViewerId).Controller.GetAll(w.CaseId, default);

        var list = ((IEnumerable<CanvasDocumentSummaryRecord>)Assert.IsType<OkObjectResult>(result.Result).Value!).ToList();
        Assert.Equal(2, list.Count);
        Assert.Contains(list, r => r.Name == "Erin's board" && r.CreatedByName == "Erin Editor");
        Assert.Contains(list, r => r.Name == "Nora's board" && r.CreatedByName == "Nora Nodelete");
    }

    [Fact]
    public async Task An_outsider_cannot_list_or_read_a_case_board()
    {
        var w = await SeedAsync();
        var board = await CreateOnCaseAsync(w);
        var outsider = Build(w.Factory, w.OutsiderId).Controller;

        Assert.IsType<ForbidResult>((await outsider.GetAll(w.CaseId, default)).Result);
        Assert.IsType<NotFoundResult>((await outsider.GetById(board.Id, default)).Result);
    }

    [Fact]
    public async Task Reading_a_board_sends_its_revision_as_an_etag_and_its_group()
    {
        var w = await SeedAsync();
        var board = await CreateOnCaseAsync(w);
        var built = Build(w.Factory, w.ViewerId);

        var ok = Assert.IsType<OkObjectResult>((await built.Controller.GetById(board.Id, default)).Result);
        var record = (CanvasDocumentRecord)ok.Value!;

        Assert.Equal(1, record.Revision);
        Assert.Equal(w.OrgId, record.OrganizationId);
        Assert.Equal("\"1\"", built.Controller.Response.Headers.ETag.ToString());
    }

    // ── writing ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_viewer_can_read_and_not_write()
    {
        var w = await SeedAsync();
        var board = await CreateOnCaseAsync(w);

        var viewer = Build(w.Factory, w.ViewerId, ifMatch: "\"1\"");
        Assert.IsType<OkObjectResult>((await viewer.Controller.GetById(board.Id, default)).Result);
        Assert.IsType<ForbidResult>((await viewer.Controller.Create(w.CaseId, Board(), default)).Result);
        Assert.IsType<ForbidResult>((await viewer.Controller.Update(board.Id, Board(), default)).Result);
        Assert.IsType<ForbidResult>(await viewer.Controller.Delete(board.Id, default));

        viewer.Audit.VerifyNoOtherCalls();
        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal(1, (await db.CanvasDocuments.SingleAsync()).Revision);
    }

    [Fact]
    public async Task A_manager_creates_a_board_at_revision_1()
    {
        var w = await SeedAsync();

        var record = await CreateOnCaseAsync(w, title: "Henderson board");

        Assert.Equal(1, record.Revision);
        Assert.Equal(w.CaseId, record.CaseId);
        Assert.Equal("Henderson board", record.Name);
        await using var db = await w.Factory.CreateDbContextAsync();
        var stored = await db.CanvasDocuments.SingleAsync();
        Assert.Equal(1, stored.Revision);
        Assert.Equal(w.EditorId, stored.CreatedByAppUserId);
    }

    [Fact]
    public async Task A_board_with_no_title_is_named_untitled_and_a_long_title_is_cut_to_256()
    {
        var w = await SeedAsync();
        var editor = Build(w.Factory, w.EditorId).Controller;

        var untitled = JsonDocument.Parse("{\"nodes\":[]}").RootElement.Clone();
        var created = (CanvasDocumentRecord)Assert.IsType<CreatedAtActionResult>((await editor.Create(w.CaseId, untitled, default)).Result).Value!;
        Assert.Equal("Untitled board", created.Name);

        var longTitle = (CanvasDocumentRecord)Assert.IsType<CreatedAtActionResult>(
            (await editor.Create(w.CaseId, Board(new string('x', 300)), default)).Result).Value!;
        Assert.Equal(256, longTitle.Name.Length);
    }

    [Fact]
    public async Task A_body_that_is_not_a_json_object_is_refused()
    {
        var w = await SeedAsync();
        var array = JsonDocument.Parse("[1,2,3]").RootElement.Clone();

        var result = await Build(w.Factory, w.EditorId).Controller.Create(w.CaseId, array, default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Creating_on_a_case_that_does_not_exist_is_404()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, w.EditorId).Controller.Create(Guid.NewGuid(), Board(), default);
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Saving_with_the_loaded_revision_bumps_it()
    {
        var w = await SeedAsync();
        var board = await CreateOnCaseAsync(w);

        var built = Build(w.Factory, w.NoDeleteId, ifMatch: "\"1\"");
        var ok = Assert.IsType<OkObjectResult>((await built.Controller.Update(board.Id, Board("Renamed"), default)).Result);
        var record = (CanvasDocumentRecord)ok.Value!;

        Assert.Equal(2, record.Revision);
        Assert.Equal("Renamed", record.Name);
        Assert.Equal("\"2\"", built.Controller.Response.Headers.ETag.ToString());
        await using var db = await w.Factory.CreateDbContextAsync();
        var stored = await db.CanvasDocuments.SingleAsync();
        Assert.Equal(2, stored.Revision);
        Assert.Equal(w.NoDeleteId, stored.UpdatedByAppUserId);
    }

    [Fact]
    public async Task An_unquoted_if_match_is_accepted_too()
    {
        var w = await SeedAsync();
        var board = await CreateOnCaseAsync(w);

        var result = await Build(w.Factory, w.EditorId, ifMatch: "1").Controller.Update(board.Id, Board(), default);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Saving_with_a_stale_revision_answers_409_with_the_server_copy()
    {
        var w = await SeedAsync();
        var board = await CreateOnCaseAsync(w);
        Assert.IsType<OkObjectResult>((await Build(w.Factory, w.EditorId, "\"1\"").Controller
            .Update(board.Id, Board("Theirs"), default)).Result);

        var stale = await Build(w.Factory, w.NoDeleteId, "\"1\"").Controller.Update(board.Id, Board("Mine"), default);

        var conflict = Assert.IsType<ConflictObjectResult>(stale.Result);
        var server = (CanvasDocumentRecord)conflict.Value!;
        Assert.Equal(2, server.Revision);
        Assert.Equal("Theirs", server.Name);
        await using var db = await w.Factory.CreateDbContextAsync();
        var stored = await db.CanvasDocuments.SingleAsync();
        Assert.Equal(server.DocumentJson, stored.DocumentJson);
        Assert.Equal("Theirs", stored.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\"abc\"")]
    [InlineData("*")]
    public async Task Saving_without_a_readable_If_Match_is_428(string? ifMatch)
    {
        var w = await SeedAsync();
        var board = await CreateOnCaseAsync(w);

        var result = await Build(w.Factory, w.EditorId, ifMatch).Controller.Update(board.Id, Board(), default);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(428, status.StatusCode);
        Assert.Equal("Send If-Match with the revision you loaded.", status.Value);
    }

    [Fact]
    public async Task A_read_only_organisation_cannot_save()
    {
        var w = await SeedAsync();
        var board = await CreateOnCaseAsync(w);
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = w.OrgId, Status = SubscriptionStatus.Lapsed,
                Interval = BillingInterval.Monthly, DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.EditorId,
            });
            await db.SaveChangesAsync();
        }
        var editor = Build(w.Factory, w.EditorId, "\"1\"").Controller;

        var put = Assert.IsType<BadRequestObjectResult>((await editor.Update(board.Id, Board(), default)).Result);
        var post = Assert.IsType<BadRequestObjectResult>((await editor.Create(w.CaseId, Board(), default)).Result);

        Assert.Contains("subscription has ended", (string)put.Value!);
        Assert.Contains("subscription has ended", (string)post.Value!);
    }

    [Fact]
    public async Task A_personal_board_is_only_its_authors()
    {
        var w = await SeedAsync();
        var mine = (CanvasDocumentRecord)Assert.IsType<CreatedAtActionResult>(
            (await Build(w.Factory, w.EditorId).Controller.Create(null, Board("My sketch"), default)).Result).Value!;
        Assert.Null(mine.CaseId);

        var other = Build(w.Factory, w.ViewerId, "\"1\"").Controller;
        Assert.IsType<NotFoundResult>((await other.GetById(mine.Id, default)).Result);
        Assert.IsType<NotFoundResult>((await other.Update(mine.Id, Board("Hijacked"), default)).Result);
        Assert.IsType<NotFoundResult>(await other.Delete(mine.Id, default));

        var myList = (IEnumerable<CanvasDocumentSummaryRecord>)Assert.IsType<OkObjectResult>(
            (await Build(w.Factory, w.EditorId).Controller.GetAll(null, default)).Result).Value!;
        Assert.Single(myList);
        var theirList = (IEnumerable<CanvasDocumentSummaryRecord>)Assert.IsType<OkObjectResult>(
            (await other.GetAll(null, default)).Result).Value!;
        Assert.Empty(theirList);

        Assert.IsType<OkObjectResult>((await Build(w.Factory, w.EditorId, "\"1\"").Controller
            .Update(mine.Id, Board("Still mine"), default)).Result);
    }

    [Fact]
    public async Task Delete_requires_Cases_Delete()
    {
        var w = await SeedAsync();
        var board = await CreateOnCaseAsync(w);

        Assert.IsType<ForbidResult>(await Build(w.Factory, w.NoDeleteId).Controller.Delete(board.Id, default));
        Assert.IsType<NoContentResult>(await Build(w.Factory, w.EditorId).Controller.Delete(board.Id, default));

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Empty(await db.CanvasDocuments.ToListAsync());
    }

    [Fact]
    public async Task Every_mutation_writes_one_audit_row_without_the_document()
    {
        var w = await SeedAsync();

        var create = Build(w.Factory, w.EditorId);
        var created = (CanvasDocumentRecord)Assert.IsType<CreatedAtActionResult>(
            (await create.Controller.Create(w.CaseId, Board(), default)).Result).Value!;
        create.Audit.Verify(a => a.LogCreateAsync(nameof(CanvasDocument), created.Id, It.IsAny<object>(), w.EditorId, AppSources.WebApi), Times.Once);
        create.Audit.VerifyNoOtherCalls();

        var update = Build(w.Factory, w.EditorId, "\"1\"");
        Assert.IsType<OkObjectResult>((await update.Controller.Update(created.Id, Board(), default)).Result);
        update.Audit.Verify(a => a.LogUpdateAsync(nameof(CanvasDocument), created.Id, It.IsAny<object>(), It.IsAny<object>(), w.EditorId, AppSources.WebApi), Times.Once);
        update.Audit.VerifyNoOtherCalls();

        var delete = Build(w.Factory, w.EditorId);
        Assert.IsType<NoContentResult>(await delete.Controller.Delete(created.Id, default));
        delete.Audit.Verify(a => a.LogDeleteAsync(nameof(CanvasDocument), created.Id, It.IsAny<object>(), w.EditorId, AppSources.WebApi), Times.Once);
        delete.Audit.VerifyNoOtherCalls();

        // The document itself never goes into the audit log: a board can be megabytes, and it holds
        // witness names the audit log has no business copying.
        foreach (var invocation in create.Audit.Invocations.Concat(update.Audit.Invocations).Concat(delete.Audit.Invocations))
            foreach (var argument in invocation.Arguments)
                Assert.DoesNotContain("Knocking at 2am", JsonSerializer.Serialize(argument));
    }

    // ── what a save does to the markup (R2b) ─────────────────────────────────

    [Fact]
    public async Task A_stored_onerror_is_gone_when_the_board_is_read_back()
    {
        var w = await SeedAsync();
        var hostile = Board(html: "<p>Hi</p><img src=\"x\" onerror=\"alert(document.cookie)\"><script>alert(1)</script>");

        var created = (CanvasDocumentRecord)Assert.IsType<CreatedAtActionResult>(
            (await Build(w.Factory, w.EditorId).Controller.Create(w.CaseId, hostile, default)).Result).Value!;
        var read = (CanvasDocumentRecord)Assert.IsType<OkObjectResult>(
            (await Build(w.Factory, w.ViewerId).Controller.GetById(created.Id, default)).Result).Value!;

        Assert.DoesNotContain("onerror", read.DocumentJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", read.DocumentJson, StringComparison.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(read.DocumentJson);
        var html = doc.RootElement.GetProperty("nodes")[0].GetProperty("data").GetProperty("html").GetString();
        Assert.Contains("<p>Hi</p>", html);

        // A text block's text is not markup and is left exactly as typed.
        Assert.Equal("<b>not markup, just text</b>",
            doc.RootElement.GetProperty("nodes")[1].GetProperty("data").GetProperty("text").GetString());
    }

    [Fact]
    public async Task A_save_through_PUT_is_sanitised_too()
    {
        var w = await SeedAsync();
        var board = await CreateOnCaseAsync(w);

        var ok = Assert.IsType<OkObjectResult>((await Build(w.Factory, w.EditorId, "\"1\"").Controller
            .Update(board.Id, Board(html: "<a href=\"javascript:alert(1)\">x</a><img src=x onerror=alert(1)>"), default)).Result);

        var json = ((CanvasDocumentRecord)ok.Value!).DocumentJson;
        Assert.DoesNotContain("onerror", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", json, StringComparison.OrdinalIgnoreCase);
    }

    // ── the race (R20) ────────────────────────────────────────────────────────

    /// <summary>Runs a callback once, just before the next SaveChanges reaches the database.</summary>
    private sealed class OneShotBeforeSave : SaveChangesInterceptor
    {
        public Func<Task>? Next;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref Next, null) is { } run) await run();
            return result;
        }
    }

    /// <summary>
    /// Two saves that both loaded revision 1 and both passed the compare: one gets 200, the other
    /// 409 — decided by the UPDATE's <c>WHERE Revision = 1</c>, not by who read first.
    /// </summary>
    [Fact]
    public async Task Two_racing_saves_of_the_same_revision_one_wins_and_one_conflicts()
    {
        var hook = new OneShotBeforeSave();
        await using var sqlite = await SqliteTestDb.CreateAsync(hook);
        var w = await SeedAsync(sqlite.Factory);
        var board = await CreateOnCaseAsync(w);

        ActionResult<CanvasDocumentRecord>? second = null;
        // The first save has read revision 1 and passed the compare; before its UPDATE reaches the
        // database, the second save — which also read revision 1 — runs to completion.
        hook.Next = async () =>
            second = await Build(w.Factory, w.NoDeleteId, "\"1\"").Controller.Update(board.Id, Board("Second"), default);

        var first = await Build(w.Factory, w.EditorId, "\"1\"").Controller.Update(board.Id, Board("First"), default);

        Assert.NotNull(second);
        Assert.IsType<OkObjectResult>(second!.Result);
        var conflict = Assert.IsType<ConflictObjectResult>(first.Result);
        Assert.Equal(2, ((CanvasDocumentRecord)conflict.Value!).Revision);
        Assert.Equal("Second", ((CanvasDocumentRecord)conflict.Value!).Name);

        await using var db = await sqlite.NewContextAsync();
        var stored = await db.CanvasDocuments.SingleAsync();
        Assert.Equal(2, stored.Revision);
        Assert.Equal("Second", stored.Name);
    }
}
