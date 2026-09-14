using Ben.Data.Common.Blocks;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Research pages: drafts that stay their author's until published, saves that cannot overwrite newer work, and pages
/// that hold only what is safe to draw (beta feedback, 2026-09-14).
/// </summary>
/// <remarks>
/// On <see cref="SqliteTestDb"/> because a draft save is a conditional <c>ExecuteUpdate</c> — the in-memory provider
/// cannot run it — and because that condition is the rule under test.
/// </remarks>
public sealed class ResearchPageTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _caseId = Guid.NewGuid();
    private readonly Guid _author = Guid.NewGuid();
    private readonly Guid _colleague = Guid.NewGuid();
    private readonly Mock<IFileStorageService> _storage = new();
    private FakeLinkPreviews _previews = null!;

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        _previews = new FakeLinkPreviews(_sqlite.Factory);
        await using var db = await _sqlite.NewContextAsync();
        var now = DateTime.UtcNow;
        db.Users.Add(new AppUser { Id = _author, UserName = "sarah@t.test", Email = "sarah@t.test", DisplayName = "Sarah Mitchell", DateCreated = now });
        db.Users.Add(new AppUser { Id = _colleague, UserName = "james@t.test", Email = "james@t.test", DisplayName = "James Thornton", DateCreated = now });
        db.Organizations.Add(new Organization { Id = _orgId, Name = "Org", UrlName = "org", DateCreated = now, CreatedByAppUserId = _author });
        foreach (var member in new[] { _author, _colleague })
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = _orgId, AppUserId = member, Role = OrganizationMemberRole.Manager,
                IsActive = true, DateCreated = now, CreatedByAppUserId = _author,
            });
        db.Cases.Add(new Case
        {
            Id = _caseId, OrganizationId = _orgId, Title = "Case", CaseYear = 2026, OrgCaseNumber = 1,
            StreetAddress1 = "1 Main", City = "Franklin", State = "TN", ZipCode = "37064", Country = "US",
            DateCreated = now, CreatedByAppUserId = _author,
        });
        db.UploadFileTypes.Add(new UploadFileType
        {
            Id = new Guid("20000000-0000-0000-0000-000000000001"), Name = "Case Evidence", DateCreated = now, CreatedByAppUserId = _author,
        });
        await db.SaveChangesAsync();
        await TestSeeds.BridgeAsync(_sqlite.Factory, _orgId, TestSeeds.CaseWork);

        _storage.Setup(s => s.CaseFilePath(It.IsAny<Guid>(), It.IsAny<string>())).Returns<Guid, string>((c, p) => $"cases/{c}/{p}");
        _storage.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    public async Task DisposeAsync() => await _sqlite.DisposeAsync();

    private CaseResearchController As(Guid userId) => new(
        _sqlite.Factory, _storage.Object, TestMedia.Ingest(), TestMedia.Stripper(),
        new Ben.Service.RepositoryService.Services.OrganizationSecurityService(_sqlite.Factory), new CmsMarkupSanitizer(), _previews)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
            },
        },
    };

    private static T Value<T>(ActionResult<T> result) =>
        Assert.IsAssignableFrom<T>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private async Task<Guid> NewPageAsync(Guid by, string title = "County deeds") =>
        Value(await As(by).Create(_orgId, _caseId, new UpsertResearchRequest(CaseResearchType.Note, title, null, null), default)).Id;

    private static BlockDocument Text(string html) =>
        new() { Blocks = [new Block { Id = Guid.NewGuid().ToString("N"), Kind = BlockKinds.Text, Html = html }] };

    private Task<ActionResult<ResearchDraftSavedDto>> SaveAsync(Guid by, Guid page, BlockDocument doc, int baseRevision, Guid? saveId = null, string title = "County deeds") =>
        As(by).SaveDraft(_orgId, _caseId, page, new SaveResearchDraftRequest(doc, baseRevision, saveId ?? Guid.NewGuid(), title, null), default);

    private static IFormFile Upload(string name, string contentType, byte[] bytes) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name) { Headers = new HeaderDictionary { ["Content-Type"] = contentType } };

    // ── Drafts and publishing ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_new_page_is_its_authors_draft_and_nobody_else_sees_it()
    {
        var page = await NewPageAsync(_author);

        var authorsList = Value(await As(_author).GetAll(_orgId, _caseId, default));
        Assert.Contains(authorsList, e => e.Id == page && e.HasUnpublishedDraft && e.DraftIsMine);

        Assert.DoesNotContain(Value(await As(_colleague).GetAll(_orgId, _caseId, default)), e => e.Id == page);
        Assert.IsType<NotFoundResult>((await As(_colleague).GetPage(_orgId, _caseId, page, default)).Result);
    }

    [Fact]
    public async Task A_saved_draft_comes_back_to_its_author_and_publishing_shows_it_to_everyone()
    {
        var page = await NewPageAsync(_author);
        var saved = Value(await SaveAsync(_author, page, Text("<p>Deed of <strong>1911</strong>.</p>"), baseRevision: 1));
        Assert.Equal(2, saved.Revision);

        var draft = Value(await As(_author).GetPage(_orgId, _caseId, page, default));
        Assert.True(draft.IsDraft);
        Assert.Contains("<strong>1911</strong>", draft.Document.Blocks.Single().Html);

        var published = Value(await As(_author).Publish(_orgId, _caseId, page, default));
        Assert.False(published.IsDraft);
        Assert.False(published.HasUnpublishedDraft);

        var colleagueView = Value(await As(_colleague).GetPage(_orgId, _caseId, page, default));
        Assert.Contains("1911", colleagueView.Document.Blocks.Single().Html);
        var listed = Assert.Single(Value(await As(_colleague).GetAll(_orgId, _caseId, default)), e => e.Id == page);
        Assert.Equal("Deed of 1911.", listed.Excerpt);
    }

    [Fact]
    public async Task Edits_after_publishing_are_a_new_draft_the_group_does_not_see_until_published_again()
    {
        var page = await NewPageAsync(_author);
        await SaveAsync(_author, page, Text("<p>First version</p>"), 1);
        await As(_author).Publish(_orgId, _caseId, page, default);

        Value(await SaveAsync(_author, page, Text("<p>Second version</p>"), 2));

        var colleague = Value(await As(_colleague).GetPage(_orgId, _caseId, page, default));
        Assert.Contains("First version", colleague.Document.Blocks.Single().Html);
        Assert.True(colleague.HasUnpublishedDraft);
        Assert.Equal("Sarah Mitchell", colleague.DraftHeldByName);

        var author = Value(await As(_author).GetPage(_orgId, _caseId, page, default));
        Assert.Contains("Second version", author.Document.Blocks.Single().Html);
    }

    [Fact]
    public async Task Nobody_saves_over_someone_elses_unpublished_draft()
    {
        var page = await NewPageAsync(_author);
        await SaveAsync(_author, page, Text("<p>Mine</p>"), 1);
        await As(_author).Publish(_orgId, _caseId, page, default);
        await SaveAsync(_author, page, Text("<p>Still mine, unpublished</p>"), 2);

        var refused = await SaveAsync(_colleague, page, Text("<p>Theirs</p>"), 3);
        var conflict = Assert.IsType<ConflictObjectResult>(refused.Result);
        Assert.Contains("Sarah Mitchell", (string)conflict.Value!);
        Assert.IsType<ConflictObjectResult>((await As(_colleague).Publish(_orgId, _caseId, page, default)).Result);
    }

    [Fact]
    public async Task A_save_made_against_an_older_revision_is_refused()
    {
        var page = await NewPageAsync(_author);
        Value(await SaveAsync(_author, page, Text("<p>Tab one</p>"), 1));

        var stale = await SaveAsync(_author, page, Text("<p>Tab two, opened before</p>"), 1);
        Assert.IsType<ConflictObjectResult>(stale.Result);

        var draft = Value(await As(_author).GetPage(_orgId, _caseId, page, default));
        Assert.Contains("Tab one", draft.Document.Blocks.Single().Html);
    }

    [Fact]
    public async Task The_same_save_sent_twice_is_answered_once()
    {
        var page = await NewPageAsync(_author);
        var saveId = Guid.NewGuid();
        var first = Value(await SaveAsync(_author, page, Text("<p>Once</p>"), 1, saveId));
        var retry = Value(await SaveAsync(_author, page, Text("<p>Once</p>"), 1, saveId));

        Assert.Equal(first.Revision, retry.Revision);
        await using var db = await _sqlite.NewContextAsync();
        Assert.Equal(2, (await db.CaseResearchEntries.SingleAsync(e => e.Id == page)).DraftRevision);
    }

    [Fact]
    public async Task A_member_who_may_only_read_cannot_save_a_draft()
    {
        var reader = Guid.NewGuid();
        await using (var db = await _sqlite.NewContextAsync())
        {
            db.Users.Add(new AppUser { Id = reader, UserName = "r@t.test", Email = "r@t.test", DateCreated = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var page = await NewPageAsync(_author);
        Assert.IsType<ForbidResult>((await SaveAsync(reader, page, Text("<p>x</p>"), 1)).Result);
    }

    [Fact]
    public async Task A_note_from_before_pages_reads_as_a_published_page_of_one_text_block()
    {
        var legacy = Guid.NewGuid();
        await using (var db = await _sqlite.NewContextAsync())
        {
            db.CaseResearchEntries.Add(new CaseResearchEntry
            {
                Id = legacy, CaseId = _caseId, ResearchType = CaseResearchType.Note, Title = "Old note",
                Body = "Line one\nLine two<script>x</script>", DateCreated = DateTime.UtcNow.AddDays(-9), CreatedByAppUserId = _author,
            });
            await db.SaveChangesAsync();
        }

        var page = Value(await As(_colleague).GetPage(_orgId, _caseId, legacy, default));
        Assert.False(page.IsDraft);
        Assert.NotNull(page.PublishedUtc);
        var block = Assert.Single(page.Document.Blocks);
        Assert.Equal(BlockKinds.Text, block.Kind);
        Assert.DoesNotContain("<script", block.Html);
        Assert.Contains("Line one", block.Html);
    }

    // ── What a page may hold ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Text_is_sanitized_and_pictures_pasted_from_our_own_media_addresses_are_removed()
    {
        var page = await NewPageAsync(_author);
        await SaveAsync(_author, page, Text(
            "<p onclick=\"x()\">Hi<script>alert(1)</script></p><img src=\"/media/file/abc?t=ticket\"><img src=\"https://example.com/a.png\">"), 1);

        var html = Value(await As(_author).GetPage(_orgId, _caseId, page, default)).Document.Blocks.Single().Html!;
        Assert.DoesNotContain("script", html);
        Assert.DoesNotContain("onclick", html);
        Assert.DoesNotContain("/media/", html);
        Assert.Contains("https://example.com/a.png", html);
    }

    [Fact]
    public async Task A_kind_of_block_the_site_does_not_know_is_refused()
    {
        var page = await NewPageAsync(_author);
        var doc = new BlockDocument { Blocks = [new Block { Id = "t", Kind = "table" }] };
        Assert.IsType<BadRequestObjectResult>((await SaveAsync(_author, page, doc, 1)).Result);
    }

    [Fact]
    public async Task A_picture_or_file_must_be_one_uploaded_to_this_page()
    {
        var page = await NewPageAsync(_author);
        var doc = new BlockDocument { Blocks = [new Block { Id = "f", Kind = BlockKinds.File, UploadFileId = Guid.NewGuid() }] };
        Assert.IsType<BadRequestObjectResult>((await SaveAsync(_author, page, doc, 1)).Result);
    }

    [Fact]
    public async Task A_file_uploaded_to_the_rail_can_be_placed_and_then_cannot_be_deleted_while_shown()
    {
        var page = await NewPageAsync(_author);
        var attachment = Value(await As(_author).UploadAttachment(_orgId, _caseId, page,
            Upload("deed.txt", "text/plain", "Deed of 1911"u8.ToArray()), default));
        Assert.Equal(CaseResearchAttachmentKind.File, attachment.Kind);

        // The editor claims a different name and size; the stored upload's own are what the page keeps.
        var doc = new BlockDocument
        {
            Blocks = [new Block { Id = "f", Kind = BlockKinds.File, UploadFileId = attachment.FileId, FileName = "evil.exe", FileSize = 1 }],
        };
        Value(await SaveAsync(_author, page, doc, 1));
        var block = Value(await As(_author).GetPage(_orgId, _caseId, page, default)).Document.Blocks.Single();
        Assert.Equal("deed.txt", block.FileName);

        Assert.IsType<ConflictObjectResult>(await As(_author).DeleteAttachment(_orgId, _caseId, page, attachment.Id, default));

        // A non-picture cannot be shown as a picture.
        var asImage = new BlockDocument { Blocks = [new Block { Id = "i", Kind = BlockKinds.Image, UploadFileId = attachment.FileId }] };
        Assert.IsType<BadRequestObjectResult>((await SaveAsync(_author, page, asImage, 2)).Result);
    }

    [Fact]
    public async Task A_link_card_keeps_its_domain_from_the_address_and_only_our_own_preview_pictures()
    {
        var page = await NewPageAsync(_author);
        var ours = $"/media/link-preview/{Guid.NewGuid()}";
        var doc = new BlockDocument
        {
            Blocks =
            [
                new Block { Id = "a", Kind = BlockKinds.Link, Url = "https://findagrave.example/memorial/1", PreviewDomain = "trusted-bank.example",
                            PreviewTitle = "<b>Grave</b>", PreviewImageUrl = ours },
                new Block { Id = "b", Kind = BlockKinds.Link, Url = "https://example.com/", PreviewImageUrl = "https://tracker.example/pixel.gif" },
            ],
        };
        Value(await SaveAsync(_author, page, doc, 1));

        var blocks = Value(await As(_author).GetPage(_orgId, _caseId, page, default)).Document.Blocks;
        Assert.Equal("findagrave.example", blocks[0].PreviewDomain);
        Assert.Equal("Grave", blocks[0].PreviewTitle);
        Assert.Equal(ours, blocks[0].PreviewImageUrl);
        Assert.Null(blocks[1].PreviewImageUrl);

        var notAWebAddress = new BlockDocument { Blocks = [new Block { Id = "c", Kind = BlockKinds.Link, Url = "javascript:alert(1)" }] };
        Assert.IsType<BadRequestObjectResult>((await SaveAsync(_author, page, notAWebAddress, 2)).Result);
    }

    [Fact]
    public async Task A_map_keeps_places_on_earth_and_falls_back_to_no_route_under_two_stops()
    {
        var page = await NewPageAsync(_author);
        var oneStop = new BlockDocument
        {
            Blocks = [new Block { Id = "m", Kind = BlockKinds.Map, MapRoute = BlockMapRoutes.Walking, Zoom = 99,
                                  MapStops = [new BlockMapStop(35.9251, -86.8689, "Rest Haven Cemetery")] }],
        };
        Value(await SaveAsync(_author, page, oneStop, 1));
        var map = Value(await As(_author).GetPage(_orgId, _caseId, page, default)).Document.Blocks.Single();
        Assert.Equal(BlockMapRoutes.None, map.MapRoute);
        Assert.Equal(20, map.Zoom);

        var offEarth = new BlockDocument { Blocks = [new Block { Id = "m", Kind = BlockKinds.Map, MapStops = [new BlockMapStop(91, 0)] }] };
        Assert.IsType<BadRequestObjectResult>((await SaveAsync(_author, page, offEarth, 2)).Result);

        var tooMany = new BlockDocument
        {
            Blocks = [new Block { Id = "m", Kind = BlockKinds.Map, MapStops = Enumerable.Range(0, 11).Select(i => new BlockMapStop(35 + i * .01, -86)).ToList() }],
        };
        Assert.IsType<BadRequestObjectResult>((await SaveAsync(_author, page, tooMany, 2)).Result);

        var unknownRoute = new BlockDocument
        {
            Blocks = [new Block { Id = "m", Kind = BlockKinds.Map, MapRoute = "teleport", MapStops = [new BlockMapStop(35, -86), new BlockMapStop(35.1, -86)] }],
        };
        Assert.IsType<BadRequestObjectResult>((await SaveAsync(_author, page, unknownRoute, 2)).Result);
    }

    [Fact]
    public async Task A_link_in_the_rail_is_kept_once_per_address_with_its_card_and_must_be_a_web_address()
    {
        var page = await NewPageAsync(_author);
        var first = Value(await As(_author).AddLinkAttachment(_orgId, _caseId, page, new AddResearchLinkRequest("https://example.com/census"), default));
        var again = Value(await As(_author).AddLinkAttachment(_orgId, _caseId, page, new AddResearchLinkRequest("https://example.com/census"), default));
        Assert.Equal(first.Id, again.Id);
        Assert.Equal("Page at example.com", first.Preview?.Title);
        Assert.Equal("Page at example.com", first.Title);
        Assert.Single(_previews.Fetched);   // the second paste used the kept card

        Value(await As(_author).AddLinkAttachment(_orgId, _caseId, page, new AddResearchLinkRequest("https://example.com/census", RefreshPreview: true), default));
        Assert.Equal(2, _previews.Fetched.Count);

        var rail = Value(await As(_author).GetPage(_orgId, _caseId, page, default)).Attachments;
        Assert.Equal("example.com", Assert.Single(rail).Preview?.Domain);

        Assert.IsType<BadRequestObjectResult>((await As(_author).AddLinkAttachment(_orgId, _caseId, page, new AddResearchLinkRequest("ftp://example.com"), default)).Result);
    }

    [Fact]
    public async Task Deleting_a_page_removes_its_rail_and_the_files_in_it()
    {
        var page = await NewPageAsync(_author);
        var attachment = Value(await As(_author).UploadAttachment(_orgId, _caseId, page, Upload("a.txt", "text/plain", "abc"u8.ToArray()), default));

        Assert.IsType<NoContentResult>(await As(_author).Delete(_orgId, _caseId, page, default));

        await using var db = await _sqlite.NewContextAsync();
        Assert.False(await db.CaseResearchAttachments.AnyAsync());
        Assert.False(await db.UploadFiles.AnyAsync(f => f.Id == attachment.FileId));
        _storage.Verify(s => s.DeleteAsync(It.Is<string>(p => p.Contains("/research/")), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}
