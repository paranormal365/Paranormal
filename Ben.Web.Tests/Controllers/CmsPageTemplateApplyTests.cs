using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using System.Text.Json;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Starting a new page from one of the group's saved page layouts (backlog item #80, part 2b).
/// </summary>
/// <remarks>
/// <para><b>The storage for this already existed and nothing could reach it.</b>
/// <c>CmsTemplateScope.Page</c> was defined, saved, listed, updated, deleted and sanitized — and no
/// screen or endpoint ever created a page from one. The sixth write-only feature in this codebase;
/// see the reachability note in <c>ReachableComponentTests</c>.</para>
///
/// <para><b>Copied, not referenced</b>, matching the decision recorded on the entity: tidying a
/// template next year must not rewrite a page that has been live since. So the test that matters
/// most is the one proving an edit to the template leaves the page alone.</para>
/// </remarks>
public sealed class CmsPageTemplateApplyTests
{
    private static readonly Guid OrgId  = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>
    /// Maps sections for real rather than returning an empty array.
    /// </summary>
    /// <remarks>
    /// The existing suite's mapper mock returns <c>Array.Empty</c>, which would make every
    /// assertion here vacuously true — the response would carry no sections whether or not the
    /// template was applied.
    /// </remarks>
    private static IMapper CreateMapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<IReadOnlyList<CmsSectionRecord>>(It.IsAny<object>()))
         .Returns<object>(o => o is not IEnumerable<CmsSection> sections
             ? []
             : [.. sections.Select(s => new CmsSectionRecord
             {
                 Id = s.Id,
                 OrganizationPageId = s.OrganizationPageId,
                 SectionType = s.SectionType,
                 Title = s.Title,
                 ContentJson = s.ContentJson,
                 SortOrder = s.SortOrder,
                 IsActive = s.IsActive,
             })]);
        return m.Object;
    }

    private static OrgCmsPageController Build(IDbContextFactory<BenDataContext> factory)
        => new(factory, CreateMapper(), Mock.Of<IOrganizationSecurityService>(),
               Mock.Of<IAuditLogService>(), new CmsMarkupSanitizer())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
                        new Claim(ClaimTypes.Role, RoleNames.SuperAdmin),
                    ], "Bearer"))
                }
            }
        };

    private static async Task<Guid> SeedTemplateAsync(
        IDbContextFactory<BenDataContext> factory,
        CmsTemplateScope scope = CmsTemplateScope.Page,
        Guid? orgId = null,
        string? contentJson = null)
    {
        await using var db = await factory.CreateDbContextAsync();

        var sections = new List<CmsTemplateSectionRecord>
        {
            new(CmsSectionType.RichText, "What we found", """{"html":"<p>Findings go here.</p>"}""", 0),
            new(CmsSectionType.EmbeddedInvestigations, "The visit", """{"ids":[]}""", 1),
        };

        var id = Guid.NewGuid();
        db.OrganizationCmsTemplates.Add(new OrganizationCmsTemplate
        {
            Id = id, OrganizationId = orgId ?? OrgId,
            Name = "Investigation Results", Description = "Our standard write-up",
            Scope = scope, SectionType = CmsSectionType.RichText,
            ContentJson = contentJson ?? JsonSerializer.Serialize(sections),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
        });

        await db.SaveChangesAsync();
        return id;
    }

    private static CreateCmsPageRequest Request(Guid? templateId, string slug = "results")
        => new("Investigation Results", slug, null, IsPublic: true, null, 0, templateId);

    private static async Task<CmsPageDetailResponse> CreateAsync(
        IDbContextFactory<BenDataContext> factory, Guid? templateId, string slug = "results")
    {
        var result = await Build(factory).Create(OrgId, Request(templateId, slug), default);
        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        return Assert.IsType<CmsPageDetailResponse>(created.Value);
    }

    // ── Applying a template ──────────────────────────────────────────────────

    /// <summary>The whole point: the new page arrives with the layout already on it.</summary>
    [Fact]
    public async Task A_page_started_from_a_template_gets_its_sections()
    {
        var factory = CreateFactory();
        var templateId = await SeedTemplateAsync(factory);

        var page = await CreateAsync(factory, templateId);

        Assert.Equal(2, page.Sections.Count);
        Assert.Equal("What we found", page.Sections[0].Title);
        Assert.Equal("The visit", page.Sections[1].Title);
    }

    [Fact]
    public async Task The_sections_keep_the_templates_order_and_types()
    {
        var factory = CreateFactory();
        var templateId = await SeedTemplateAsync(factory);

        var page = await CreateAsync(factory, templateId);

        Assert.Equal(CmsSectionType.RichText, page.Sections[0].SectionType);
        Assert.Equal(CmsSectionType.EmbeddedInvestigations, page.Sections[1].SectionType);
        Assert.Equal([0, 1], page.Sections.Select(s => s.SortOrder));
    }

    [Fact]
    public async Task The_sections_are_really_saved_not_just_returned()
    {
        var factory = CreateFactory();
        var templateId = await SeedTemplateAsync(factory);

        var page = await CreateAsync(factory, templateId);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(2, await db.CmsSections.CountAsync(s => s.OrganizationPageId == page.Id));
    }

    /// <summary>Creating a page without naming a template still works, and makes a bare page.</summary>
    [Fact]
    public async Task A_page_with_no_template_is_created_empty()
    {
        var factory = CreateFactory();
        await SeedTemplateAsync(factory);

        var page = await CreateAsync(factory, templateId: null);

        Assert.Empty(page.Sections);
    }

    // ── Copied, not referenced ───────────────────────────────────────────────

    /// <summary>
    /// The decision recorded on the entity, proven: editing the template afterwards leaves the page
    /// exactly as it was. A reference would be more powerful and much more surprising — nobody
    /// expects tidying a template to rewrite a page that has been live for a year.
    /// </summary>
    [Fact]
    public async Task Editing_the_template_afterwards_does_not_touch_the_page()
    {
        var factory = CreateFactory();
        var templateId = await SeedTemplateAsync(factory);
        var page = await CreateAsync(factory, templateId);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var template = await db.OrganizationCmsTemplates.FirstAsync(t => t.Id == templateId);
            template.ContentJson = JsonSerializer.Serialize(new List<CmsTemplateSectionRecord>
            {
                new(CmsSectionType.RichText, "Completely different", """{"html":"<p>Rewritten.</p>"}""", 0),
            });
            await db.SaveChangesAsync();
        }

        await using var check = await factory.CreateDbContextAsync();
        var titles = await check.CmsSections
            .Where(s => s.OrganizationPageId == page.Id)
            .OrderBy(s => s.SortOrder).Select(s => s.Title).ToListAsync();

        Assert.Equal(["What we found", "The visit"], titles);
    }

    // ── What it will not apply ───────────────────────────────────────────────

    /// <summary>
    /// Another group's template is ignored — and the page is still created, because a bare page is
    /// what the caller asked for and failing the whole create over a stale id is the worse answer.
    /// </summary>
    [Fact]
    public async Task Another_organizations_template_is_ignored()
    {
        var factory = CreateFactory();
        var foreignId = await SeedTemplateAsync(factory, orgId: Guid.NewGuid());

        var page = await CreateAsync(factory, foreignId);

        Assert.Empty(page.Sections);
        Assert.NotEqual(Guid.Empty, page.Id);
    }

    /// <summary>A section-scoped template is not a page layout and is not treated as one.</summary>
    [Fact]
    public async Task A_section_scoped_template_is_ignored()
    {
        var factory = CreateFactory();
        var sectionScoped = await SeedTemplateAsync(factory, scope: CmsTemplateScope.Section);

        Assert.Empty((await CreateAsync(factory, sectionScoped)).Sections);
    }

    [Fact]
    public async Task A_template_that_no_longer_exists_is_ignored()
        => Assert.Empty((await CreateAsync(CreateFactory(), Guid.NewGuid())).Sections);

    /// <summary>Corrupt stored content yields a bare page rather than a broken one.</summary>
    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    public async Task Unusable_template_content_yields_a_bare_page(string contentJson)
    {
        var factory = CreateFactory();
        var templateId = await SeedTemplateAsync(factory, contentJson: contentJson);

        Assert.Empty((await CreateAsync(factory, templateId)).Sections);
    }

    // ── Sanitization ─────────────────────────────────────────────────────────

    /// <summary>
    /// Markup is cleaned on the way onto the page, not trusted because it was cleaned when the
    /// template was saved.
    /// </summary>
    /// <remarks>
    /// Ben's rule: <i>"Forms and input is not allowed on any pages of ours unless they are created
    /// by our code."</i> A template row could have been written before the sanitizer existed, or by
    /// a future path that forgets — "cleaned then" is not "clean now", and this is the last moment
    /// before the markup becomes a page.
    /// </remarks>
    [Fact]
    public async Task Template_markup_is_sanitized_on_the_way_onto_the_page()
    {
        var factory = CreateFactory();

        var hostile = JsonSerializer.Serialize(new List<CmsTemplateSectionRecord>
        {
            new(CmsSectionType.CustomHtml, "Sign up",
                """{"html":"<p>Hello</p><form action='https://evil.test'><input name='card'></form><script>alert(1)</script>"}""",
                0),
        });

        var templateId = await SeedTemplateAsync(factory, contentJson: hostile);
        var page = await CreateAsync(factory, templateId);

        var stored = Assert.Single(page.Sections).ContentJson;

        Assert.DoesNotContain("<form", stored, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<input", stored, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", stored, StringComparison.OrdinalIgnoreCase);

        // The legitimate content survives — a sanitizer that ate the page would be its own bug.
        Assert.Contains("Hello", stored);
    }
}
