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

    /// <summary>A board whose only block is a card pointing at another board.</summary>
    private static JsonElement BoardLinkingTo(Guid target, string title = "Index board")
        => JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            title,
            nodes = new object[]
            {
                new
                {
                    id = Guid.NewGuid(), type = "Board", x = 0, y = 0, width = 300, height = 96,
                    data = new { kind = "board", documentId = target, title = "The other board" },
                },
            },
            edges = Array.Empty<object>(),
            groups = Array.Empty<object>(),
        })).RootElement;

    /// <summary>Marks a board published in the database, for tests where filing a real picture is beside the point.</summary>
    private static async Task<CanvasDocumentRecord> MarkPublishedAsync(World w, CanvasDocumentRecord board)
    {
        await using var db = await w.Factory.CreateDbContextAsync();
        var entity = await db.CanvasDocuments.SingleAsync(d => d.Id == board.Id);
        entity.PublishedJson = entity.DocumentJson;
        entity.PublishedRevision = entity.Revision;
        await db.SaveChangesAsync();
        return board;
    }

    /// <summary>
    /// A board the group can see. Publishing is what shows a board to anybody but its writer (Ben, 2026-09-16), so a
    /// test about reading or saving mechanics starts from a published one.
    /// </summary>
    private static async Task<CanvasDocumentRecord> CreatePublishedOnCaseAsync(World w, Guid? asUser = null, string title = "Henderson board")
    {
        var board = await CreateOnCaseAsync(w, asUser, title);
        var built = Build(w.Factory, asUser ?? w.EditorId);
        IngestSucceeds(built);
        Assert.IsType<OkObjectResult>((await built.Controller.Publish(board.Id, Upload(PngBytes()), default)).Result);
        return board;
    }

    // ── the door ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Sign-in is the whole door now: the canvas is not behind a switch any more.
    /// </summary>
    /// <remarks>
    /// It was, while there were two ways to write up a case and a site had to choose. The block-editor
    /// research pages went on 2026-09-16 and boards became research itself, so a site with the switch
    /// off would have had no research at all — a flag whose off position breaks the product is not a
    /// choice worth keeping.
    /// </remarks>
    [Fact]
    public void The_controller_requires_sign_in_and_is_behind_no_feature_switch()
    {
        Assert.NotNull(typeof(CanvasDocumentController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(typeof(CanvasDocumentController).GetCustomAttribute<Ben.Data.WebApi.Services.FeatureGatedAttribute>(inherit: true));
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

    /// <summary>
    /// Ben, 2026-09-16: a board is research being written, and "they can keep the drafts which are not displayed to
    /// the members until it is published". So a case's list holds what has been published, plus the reader's own
    /// unfinished boards — never somebody else's draft.
    /// </summary>
    [Fact]
    public async Task GetAll_by_case_returns_published_boards_and_the_callers_own_drafts()
    {
        var w = await SeedAsync();
        var erins = await CreateOnCaseAsync(w, w.EditorId, "Erin's board");
        await CreateOnCaseAsync(w, w.NoDeleteId, "Nora's draft");

        var publishing = Build(w.Factory, w.EditorId);
        IngestSucceeds(publishing);
        Assert.IsType<OkObjectResult>((await publishing.Controller.Publish(erins.Id, Upload(PngBytes()), default)).Result);

        // Nora sees Erin's published board and her own draft; Erin's list holds her published board alone.
        var nora = ((IEnumerable<CanvasDocumentSummaryRecord>)Assert.IsType<OkObjectResult>(
            (await Build(w.Factory, w.NoDeleteId).Controller.GetAll(w.CaseId, default)).Result).Value!).ToList();
        Assert.Equal(2, nora.Count);
        Assert.Contains(nora, r => r.Name == "Erin's board" && r.CreatedByName == "Erin Editor" && r.IsPublished);
        Assert.Contains(nora, r => r.Name == "Nora's draft" && !r.IsPublished);

        var erin = ((IEnumerable<CanvasDocumentSummaryRecord>)Assert.IsType<OkObjectResult>(
            (await Build(w.Factory, w.EditorId).Controller.GetAll(w.CaseId, default)).Result).Value!).ToList();
        Assert.Equal(["Erin's board"], erin.Select(r => r.Name));
    }

    [Fact]
    public async Task An_unpublished_board_is_not_another_members_to_open_or_write_on()
    {
        var w = await SeedAsync();
        var draft = await CreateOnCaseAsync(w, w.EditorId, "Half a thought");

        var other = Build(w.Factory, w.NoDeleteId, ifMatch: "\"1\"").Controller;

        // Not found rather than forbidden: an unfinished board is not there for anybody else at all.
        Assert.IsType<NotFoundResult>((await other.GetById(draft.Id, default)).Result);
        Assert.IsType<NotFoundResult>((await other.Update(draft.Id, Board("Taken over"), default)).Result);
    }

    [Fact]
    public async Task Publishing_shows_the_board_to_the_group_and_later_writing_is_a_draft_again()
    {
        var w = await SeedAsync();
        var board = await CreateOnCaseAsync(w, w.EditorId, "The Henderson history");

        var publishing = Build(w.Factory, w.EditorId);
        IngestSucceeds(publishing);
        Assert.IsType<OkObjectResult>((await publishing.Controller.Publish(board.Id, Upload(PngBytes()), default)).Result);

        // Written on since: the writer works on their copy, the group still reads what was published.
        var saved = Assert.IsType<OkObjectResult>((await Build(w.Factory, w.EditorId, ifMatch: "\"1\"")
            .Controller.Update(board.Id, Board("The Henderson history", "<p>A second night</p>"), default)).Result);
        Assert.Equal(2, ((CanvasDocumentRecord)saved.Value!).Revision);

        var read = (CanvasDocumentRecord)Assert.IsType<OkObjectResult>(
            (await Build(w.Factory, w.ViewerId).Controller.GetById(board.Id, default)).Result).Value!;
        Assert.True(read.IsPublished);
        Assert.True(read.HasUnpublishedChanges);
        Assert.DoesNotContain("A second night", read.DocumentJson);

        // And the writer sees their own working copy.
        var writer = (CanvasDocumentRecord)Assert.IsType<OkObjectResult>(
            (await Build(w.Factory, w.EditorId).Controller.GetById(board.Id, default)).Result).Value!;
        Assert.Contains("A second night", writer.DocumentJson);
    }

    /// <summary>
    /// Ben, 2026-09-16: a member who may edit "can edit the board additively", and "only the author or organization
    /// admin or site admin or super admin can edit the board and change existing pieces".
    /// </summary>
    [Fact]
    public async Task Another_member_may_add_to_a_published_board_but_not_change_what_is_there()
    {
        var w = await SeedAsync();
        var board = await MarkPublishedAsync(w, await CreateOnCaseAsync(w, w.EditorId, "Erin's board"));
        var erinsCard = FirstNodeId(await ReadDocumentAsync(w, board.Id));

        // Adding a card of their own: allowed, and it becomes theirs to rework later.
        var added = await Build(w.Factory, w.NoDeleteId, "\"1\"").Controller
            .Update(board.Id, WithExtraNode(await ReadDocumentAsync(w, board.Id), "Nora's note"), default);
        Assert.IsType<OkObjectResult>(added.Result);

        // Moving Erin's card: refused, in words that say whose it is.
        var moved = await Build(w.Factory, w.NoDeleteId, "\"2\"").Controller
            .Update(board.Id, WithNodeMoved(await ReadDocumentAsync(w, board.Id), erinsCard), default);
        Assert.Contains("somebody else's work", (string)Assert.IsType<BadRequestObjectResult>(moved.Result).Value!);

        // Taking Erin's card away: refused too.
        var removed = await Build(w.Factory, w.NoDeleteId, "\"2\"").Controller
            .Update(board.Id, WithNodeRemoved(await ReadDocumentAsync(w, board.Id), erinsCard), default);
        Assert.Contains("somebody else's work", (string)Assert.IsType<BadRequestObjectResult>(removed.Result).Value!);
    }

    [Fact]
    public async Task The_author_and_a_group_administrator_may_change_what_is_already_there()
    {
        var w = await SeedAsync();
        var board = await MarkPublishedAsync(w, await CreateOnCaseAsync(w, w.EditorId, "Erin's board"));
        var card = FirstNodeId(await ReadDocumentAsync(w, board.Id));

        // The author moves their own card.
        Assert.IsType<OkObjectResult>((await Build(w.Factory, w.EditorId, "\"1\"").Controller
            .Update(board.Id, WithNodeMoved(await ReadDocumentAsync(w, board.Id), card), default)).Result);

        // A site administrator moves it as well.
        Assert.IsType<OkObjectResult>((await Build(w.Factory, w.NoDeleteId, "\"2\"", superAdmin: true).Controller
            .Update(board.Id, WithNodeMoved(await ReadDocumentAsync(w, board.Id), card), default)).Result);
    }

    [Fact]
    public async Task Opening_a_board_says_what_this_person_may_do_with_it()
    {
        var w = await SeedAsync();
        var board = await MarkPublishedAsync(w, await CreateOnCaseAsync(w, w.EditorId, "Erin's board"));

        async Task<CanvasBoardAccess> AccessFor(Guid who, bool superAdmin = false)
            => ((CanvasDocumentRecord)Assert.IsType<OkObjectResult>(
                (await Build(w.Factory, who, superAdmin: superAdmin).Controller.GetById(board.Id, default)).Result).Value!).Access;

        Assert.Equal(CanvasBoardAccess.Full, await AccessFor(w.EditorId));
        Assert.Equal(CanvasBoardAccess.Append, await AccessFor(w.NoDeleteId));
        Assert.Equal(CanvasBoardAccess.Read, await AccessFor(w.ViewerId));
        Assert.Equal(CanvasBoardAccess.Full, await AccessFor(w.ViewerId, superAdmin: true));
    }

    // ── helpers for the additive rule ─────────────────────────────────────────

    private static async Task<JsonElement> ReadDocumentAsync(World w, Guid boardId)
    {
        await using var db = await w.Factory.CreateDbContextAsync();
        var json = (await db.CanvasDocuments.AsNoTracking().SingleAsync(d => d.Id == boardId)).DocumentJson;
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static string FirstNodeId(JsonElement document)
        => document.GetProperty("nodes")[0].GetProperty("id").GetString()!;

    private static JsonElement Edit(JsonElement document, Action<System.Text.Json.Nodes.JsonNode> change)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(document.GetRawText())!;
        change(node);
        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    private static JsonElement WithExtraNode(JsonElement document, string title) => Edit(document, node =>
    {
        var added = "{\"id\":\"" + Guid.NewGuid() + "\",\"type\":\"Card\",\"x\":10,\"y\":10,\"width\":200,\"height\":120,"
                  + "\"data\":{\"templateId\":\"evidence\",\"title\":"
                  + System.Text.Json.JsonSerializer.Serialize(title) + ",\"fields\":{}}}";
        node["nodes"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse(added));
    });

    private static JsonElement WithNodeMoved(JsonElement document, string id) => Edit(document, node =>
    {
        foreach (var candidate in node["nodes"]!.AsArray())
            if (candidate!["id"]!.GetValue<string>() == id) candidate["x"] = 999;
    });

    private static JsonElement WithNodeRemoved(JsonElement document, string id) => Edit(document, node =>
    {
        var nodes = node["nodes"]!.AsArray();
        for (var i = nodes.Count - 1; i >= 0; i--)
            if (nodes[i]!["id"]!.GetValue<string>() == id) nodes.RemoveAt(i);
    });

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
        var board = await CreatePublishedOnCaseAsync(w);
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
        var board = await CreatePublishedOnCaseAsync(w);

        var viewer = Build(w.Factory, w.ViewerId, ifMatch: "\"1\"");
        Assert.IsType<OkObjectResult>((await viewer.Controller.GetById(board.Id, default)).Result);
        Assert.IsType<ForbidResult>((await viewer.Controller.Create(w.CaseId, Board(), default)).Result);
        Assert.IsType<ForbidResult>((await viewer.Controller.Update(board.Id, Board(), default)).Result);
        Assert.IsType<ForbidResult>(await viewer.Controller.Delete(board.Id, default));

        viewer.Audit.VerifyNoOtherCalls();
        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal(1, (await db.CanvasDocuments.SingleAsync()).Revision);
    }

    // ── can edit (canvas plan R33) ────────────────────────────────────────────

    [Fact]
    public async Task A_manager_is_told_the_board_is_editable_in_the_list_and_on_open()
    {
        var w = await SeedAsync();
        var board = await CreateOnCaseAsync(w);
        var editor = Build(w.Factory, w.EditorId).Controller;

        var list = (IEnumerable<CanvasDocumentSummaryRecord>)Assert.IsType<OkObjectResult>((await editor.GetAll(w.CaseId, default)).Result).Value!;
        var open = (CanvasDocumentRecord)Assert.IsType<OkObjectResult>((await editor.GetById(board.Id, default)).Result).Value!;

        Assert.True(Assert.Single(list).CanEdit);
        Assert.True(open.CanEdit);
        Assert.True(board.CanEdit);
    }

    [Fact]
    public async Task A_reader_is_told_the_board_is_view_only()
    {
        var w = await SeedAsync();
        var board = await CreatePublishedOnCaseAsync(w);
        var viewer = Build(w.Factory, w.ViewerId).Controller;

        var list = (IEnumerable<CanvasDocumentSummaryRecord>)Assert.IsType<OkObjectResult>((await viewer.GetAll(w.CaseId, default)).Result).Value!;
        var open = (CanvasDocumentRecord)Assert.IsType<OkObjectResult>((await viewer.GetById(board.Id, default)).Result).Value!;

        Assert.False(Assert.Single(list).CanEdit);
        Assert.False(open.CanEdit);
    }

    [Fact]
    public async Task A_lapsed_subscription_makes_the_board_view_only_even_for_a_manager()
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

        var open = (CanvasDocumentRecord)Assert.IsType<OkObjectResult>((await Build(w.Factory, w.EditorId).Controller.GetById(board.Id, default)).Result).Value!;

        Assert.False(open.CanEdit);
    }

    [Fact]
    public async Task A_personal_board_is_editable_by_its_author_in_the_list_and_on_open()
    {
        var w = await SeedAsync();
        var author = Build(w.Factory, w.EditorId).Controller;
        var mine = (CanvasDocumentRecord)Assert.IsType<CreatedAtActionResult>((await author.Create(null, Board("My sketch"), default)).Result).Value!;

        var list = (IEnumerable<CanvasDocumentSummaryRecord>)Assert.IsType<OkObjectResult>((await author.GetAll(null, default)).Result).Value!;
        var open = (CanvasDocumentRecord)Assert.IsType<OkObjectResult>((await author.GetById(mine.Id, default)).Result).Value!;

        Assert.True(Assert.Single(list).CanEdit);
        Assert.True(open.CanEdit);
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
        var board = await CreatePublishedOnCaseAsync(w, w.NoDeleteId);

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
        var board = await CreatePublishedOnCaseAsync(w);
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
        // Read back by the person who wrote it: what is on trial here is the cleaning, not who may see a draft.
        var read = (CanvasDocumentRecord)Assert.IsType<OkObjectResult>(
            (await Build(w.Factory, w.EditorId).Controller.GetById(created.Id, default)).Result).Value!;

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
        var board = await CreatePublishedOnCaseAsync(w);

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
        var board = await MarkPublishedAsync(w, await CreateOnCaseAsync(w));

        ActionResult<CanvasDocumentRecord>? second = null;
        // The first save has read revision 1 and passed the compare; before its UPDATE reaches the
        // database, the second save — which also read revision 1 — runs to completion.
        hook.Next = async () =>
            second = await Build(w.Factory, w.NoDeleteId, "\"1\"", superAdmin: true).Controller.Update(board.Id, Board("Second"), default);

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

    // ── publish (M6-07, R22) ──────────────────────────────────────────────────

    private static byte[] PngBytes()
    {
        using var bitmap = new SkiaSharp.SKBitmap(4, 3);
        bitmap.Erase(SkiaSharp.SKColors.DarkSlateGray);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static IFormFile Upload(byte[] bytes, string contentType = "image/png", string name = "board.png")
        => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };

    private static void IngestSucceeds(Built built)
        => built.Ingest
            .Setup(i => i.IngestAsync(It.IsAny<IFormFile>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync((IFormFile f, string path, Guid uploadId, CancellationToken _, bool _) =>
                new IngestedMedia(new UploadFileMetadata { Id = Guid.NewGuid(), UploadFileId = uploadId, MediaKind = "Image" },
                    ServedFileSize: 1234, ServedContentType: "image/jpeg", WasSanitized: true));

    [Fact]
    public async Task Publish_stores_a_board_snapshot_on_the_case_and_stamps_who_published()
    {
        var w = await SeedAsync();
        var board = await CreateOnCaseAsync(w, title: "Henderson board");
        var built = Build(w.Factory, w.NoDeleteId);
        IngestSucceeds(built);

        var ok = Assert.IsType<OkObjectResult>((await built.Controller.Publish(board.Id, Upload(PngBytes()), default)).Result);
        var record = (CanvasDocumentRecord)ok.Value!;

        await using var db = await w.Factory.CreateDbContextAsync();
        var link = await db.CaseFiles.SingleAsync();
        var upload = await db.UploadFiles.SingleAsync();
        var stored = await db.CanvasDocuments.SingleAsync();
        Assert.Equal(w.CaseId, link.CaseId);
        Assert.Equal(upload.Id, link.UploadFileId);
        Assert.Equal("Board snapshot: \"Henderson board\"", link.Description);
        Assert.Equal(Ben.Data.WebApi.SeedData.UploadFileTypeSeeder.BoardSnapshotFileTypeId, upload.UploadFileTypeId);
        Assert.Equal("Henderson board.png", upload.FileName);
        Assert.False(upload.IsPublic);
        Assert.StartsWith($"cases/{w.CaseId}/files/", upload.StoragePath);
        Assert.Equal(upload.Id, stored.PublishedUploadFileId);
        Assert.Equal(w.NoDeleteId, stored.PublishedByAppUserId);
        Assert.NotNull(stored.PublishedAtUtc);
        Assert.Equal(1, stored.Revision);   // publishing is not an edit of the document
        Assert.Equal(upload.Id, record.PublishedUploadFileId);
        built.Ingest.Verify(i => i.IngestAsync(It.IsAny<IFormFile>(), upload.StoragePath!, upload.Id, It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
        built.Audit.Verify(a => a.LogUpdateAsync(nameof(CanvasDocument), board.Id, It.IsAny<object>(), It.IsAny<object>(), w.NoDeleteId, AppSources.WebApi), Times.Once);
    }

    [Fact]
    public async Task Publishing_twice_keeps_one_snapshot()
    {
        var w = await SeedAsync();
        var board = await CreateOnCaseAsync(w);

        var first = Build(w.Factory, w.EditorId); IngestSucceeds(first);
        Assert.IsType<OkObjectResult>((await first.Controller.Publish(board.Id, Upload(PngBytes()), default)).Result);
        var second = Build(w.Factory, w.EditorId); IngestSucceeds(second);
        Assert.IsType<OkObjectResult>((await second.Controller.Publish(board.Id, Upload(PngBytes()), default)).Result);

        await using var db = await w.Factory.CreateDbContextAsync();
        var upload = await db.UploadFiles.SingleAsync();
        Assert.Equal(upload.Id, (await db.CaseFiles.SingleAsync()).UploadFileId);
        Assert.Equal(upload.Id, (await db.CanvasDocuments.SingleAsync()).PublishedUploadFileId);
        second.Ingest.Verify(i => i.DeleteAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_personal_board_cannot_be_published()
    {
        var w = await SeedAsync();
        var mine = (CanvasDocumentRecord)Assert.IsType<CreatedAtActionResult>(
            (await Build(w.Factory, w.EditorId).Controller.Create(null, Board(), default)).Result).Value!;
        var built = Build(w.Factory, w.EditorId); IngestSucceeds(built);

        var result = await built.Controller.Publish(mine.Id, Upload(PngBytes()), default);

        Assert.Equal("Save this board to a case before publishing it.",
            Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        built.Ingest.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("text/plain", false)]
    [InlineData("image/jpeg", true)]
    [InlineData("image/png", false)]    // says PNG, is not one
    public async Task Publish_refuses_anything_but_a_png(string contentType, bool realImage)
    {
        var w = await SeedAsync();
        var board = await CreateOnCaseAsync(w);
        var built = Build(w.Factory, w.EditorId); IngestSucceeds(built);
        var bytes = realImage ? PngBytes() : "not a picture at all"u8.ToArray();

        var result = await built.Controller.Publish(board.Id, Upload(bytes, contentType), default);

        Assert.Equal("The snapshot must be a PNG.", Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        built.Ingest.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_empty_snapshot_is_refused()
    {
        var w = await SeedAsync();
        var board = await CreateOnCaseAsync(w);

        var result = await Build(w.Factory, w.EditorId).Controller.Publish(board.Id, Upload([]), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task A_viewer_cannot_publish_and_an_outsider_is_told_nothing()
    {
        var w = await SeedAsync();
        var board = await CreateOnCaseAsync(w);

        var viewer = Build(w.Factory, w.ViewerId); IngestSucceeds(viewer);
        Assert.IsType<ForbidResult>((await viewer.Controller.Publish(board.Id, Upload(PngBytes()), default)).Result);
        viewer.Ingest.VerifyNoOtherCalls();
        viewer.Audit.VerifyNoOtherCalls();

        var outsider = Build(w.Factory, w.OutsiderId); IngestSucceeds(outsider);
        Assert.IsType<NotFoundResult>((await outsider.Controller.Publish(board.Id, Upload(PngBytes()), default)).Result);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Build(w.Factory, userId: null).Controller.Publish(board.Id, Upload(PngBytes()), default));
    }

    [Fact]
    public async Task Deleting_removes_the_published_snapshot()
    {
        var w = await SeedAsync();
        var board = await CreateOnCaseAsync(w);
        var publish = Build(w.Factory, w.EditorId); IngestSucceeds(publish);
        Assert.IsType<OkObjectResult>((await publish.Controller.Publish(board.Id, Upload(PngBytes()), default)).Result);

        var delete = Build(w.Factory, w.EditorId);
        Assert.IsType<NoContentResult>(await delete.Controller.Delete(board.Id, default));

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Empty(await db.UploadFiles.ToListAsync());
        Assert.Empty(await db.CaseFiles.ToListAsync());
        delete.Ingest.Verify(i => i.DeleteAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
    // ── a published board never points at an unpublished one (M9-08b) ─────────

    /// <summary>
    /// Publishing is refused while a board links to one nobody has published, and the refusal names it.
    /// </summary>
    /// <remarks>
    /// <para>Ben's rule, 2026-09-18: "the only way for it to hit the load link to other page is if
    /// the other page has been published … it will cause issues if one is not published and one that
    /// is published has a link to a page which is not published."</para>
    ///
    /// <para><b>Why the server and not only the picker.</b> The picker lists published boards only,
    /// so it stops the state being CREATED — and does nothing about a target deleted or a request
    /// crafted between the pick and the publish. What is at stake is a reader handed a door into
    /// somebody's private draft, or a dead end, so the rule is held where it cannot be bypassed.</para>
    /// </remarks>
    [Fact]
    public async Task Publishing_refuses_a_board_that_links_to_an_unpublished_board_and_names_it()
    {
        var w = await SeedAsync();
        var draft = await CreateOnCaseAsync(w, title: "A draft nobody has seen");

        var built = Build(w.Factory, w.EditorId);
        IngestSucceeds(built);
        var index = (CanvasDocumentRecord)Assert.IsType<CreatedAtActionResult>(
            (await built.Controller.Create(w.CaseId, BoardLinkingTo(draft.Id), default)).Result).Value!;

        var refused = Assert.IsType<BadRequestObjectResult>(
            (await built.Controller.Publish(index.Id, Upload(PngBytes()), default)).Result);

        Assert.Contains("A draft nobody has seen", refused.Value!.ToString());
        Assert.Contains("not been published", refused.Value!.ToString());

        // And it really did not publish.
        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Null((await db.CanvasDocuments.SingleAsync(d => d.Id == index.Id)).PublishedJson);
    }

    /// <summary>Once the target is published, the board that links to it publishes too.</summary>
    [Fact]
    public async Task Publishing_is_allowed_once_the_board_it_links_to_is_published()
    {
        var w = await SeedAsync();
        var target = await CreatePublishedOnCaseAsync(w, title: "The other board");

        var built = Build(w.Factory, w.EditorId);
        IngestSucceeds(built);
        var index = (CanvasDocumentRecord)Assert.IsType<CreatedAtActionResult>(
            (await built.Controller.Create(w.CaseId, BoardLinkingTo(target.Id), default)).Result).Value!;

        Assert.IsType<OkObjectResult>((await built.Controller.Publish(index.Id, Upload(PngBytes()), default)).Result);
    }

    /// <summary>
    /// A link to a board that no longer exists is refused too, and says so rather than naming a board
    /// it cannot find.
    /// </summary>
    [Fact]
    public async Task Publishing_refuses_a_board_that_links_to_one_that_no_longer_exists()
    {
        var w = await SeedAsync();

        var built = Build(w.Factory, w.EditorId);
        IngestSucceeds(built);
        var index = (CanvasDocumentRecord)Assert.IsType<CreatedAtActionResult>(
            (await built.Controller.Create(w.CaseId, BoardLinkingTo(Guid.NewGuid()), default)).Result).Value!;

        var refused = Assert.IsType<BadRequestObjectResult>(
            (await built.Controller.Publish(index.Id, Upload(PngBytes()), default)).Result);

        Assert.Contains("no longer exists", refused.Value!.ToString());
    }

    /// <summary>A board with no links publishes exactly as it always did.</summary>
    [Fact]
    public async Task A_board_with_no_links_still_publishes()
    {
        var w = await SeedAsync();
        var built = Build(w.Factory, w.EditorId);
        IngestSucceeds(built);
        var board = await CreateOnCaseAsync(w);

        Assert.IsType<OkObjectResult>((await built.Controller.Publish(board.Id, Upload(PngBytes()), default)).Result);
    }

    /// <summary>
    /// A board link opens the target's PUBLISHED copy, for the author too.
    /// </summary>
    /// <remarks>
    /// Otherwise checking your own link would tell you nothing about what the group can see through
    /// it. And asking for a published copy of a board that has none is, to a link, the same as gone.
    /// </remarks>
    [Fact]
    public async Task Asking_for_the_published_copy_gives_the_published_copy_even_to_its_author()
    {
        var w = await SeedAsync();
        var board = await CreatePublishedOnCaseAsync(w);

        // Change the draft after publishing, so the two copies differ. If-Match is the contract, and
        // the board is at revision 1 having just been created and published.
        var editing = Build(w.Factory, w.EditorId, ifMatch: "\"1\"");
        Assert.IsType<OkObjectResult>(
            (await editing.Controller.Update(board.Id, Board("Henderson board", "<p>Later thinking</p>"), default)).Result);

        var built = Build(w.Factory, w.EditorId);
        var published = (CanvasDocumentRecord)Assert.IsType<OkObjectResult>(
            (await built.Controller.GetById(board.Id, default, published: true)).Result).Value!;
        var draft = (CanvasDocumentRecord)Assert.IsType<OkObjectResult>(
            (await built.Controller.GetById(board.Id, default)).Result).Value!;

        Assert.DoesNotContain("Later thinking", published.DocumentJson);
        Assert.Contains("Later thinking", draft.DocumentJson);
    }

    [Fact]
    public async Task Asking_for_the_published_copy_of_an_unpublished_board_is_not_found()
    {
        var w = await SeedAsync();
        var board = await CreateOnCaseAsync(w);

        // NotFound with no body: to a link, a board with no published copy is the same as gone.
        Assert.IsAssignableFrom<NotFoundResult>(
            (await Build(w.Factory, w.EditorId).Controller.GetById(board.Id, default, published: true)).Result);
    }

}
