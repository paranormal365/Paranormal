using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The card under a link in a message (item 233, Ben 2026-09-11).
/// </summary>
/// <remarks>
/// <para>The property that matters most is the one that is NOT here: nothing in this controller
/// makes an outbound request, so a link to <c>http://169.254.169.254/</c> can only ever produce a
/// 404. The tests below hold the two halves that make that safe — an address is answered only
/// when it is one of ours, and "one of ours" is a whole-host match rather than a substring.</para>
///
/// <para>The host comparison had a real bug: the first version compared against the host the
/// REQUEST arrived on, which is the API's, so every link to our own website came back a
/// stranger's. <see cref="Our_own_website_is_recognised_even_though_the_api_answers_elsewhere"/>
/// is that bug written down.</para>
/// </remarks>
public sealed class PublicLinkPreviewControllerTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> opts) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(opts);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new BenDataContext(opts));
    }

    private const string SiteUrl = "https://ishaunted.com";

    /// <summary>The controller, answering on the API's own host — which is not the website's.</summary>
    private static PublicLinkPreviewController Build(IDbContextFactory<BenDataContext> factory,
        Ben.Data.WebApi.Services.LinkPreviews.ILinkPreviewService? kept = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppBaseUrl"] = SiteUrl })
            .Build();

        return new PublicLinkPreviewController(factory, configuration, kept ?? new NothingKept())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Request = { Host = new HostString("api.ishaunted.com") } },
            },
        };
    }

    /// <summary>A group with one published case in it.</summary>
    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync()
    {
        var factory = new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        await using var db = factory.CreateDbContext();
        var actor = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Nashville Paranormal", UrlName = "nashville-paranormal",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = actor,
        });

        db.Cases.Add(new Case
        {
            Id = Guid.NewGuid(), OrganizationId = orgId,
            Title = "The Printers Alley Knocking", UrlName = "printers-alley-knocking",
            CaseYear = 2026, OrgCaseNumber = 42,
            City = "Nashville", State = "TN",
            IsPublic = true, Status = CaseStatus.Public,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = actor,
        });

        await db.SaveChangesAsync();
        return factory;
    }

    private static async Task<LinkPreview?> PreviewAsync(IDbContextFactory<BenDataContext> factory, string url)
    {
        var result = await Build(factory).Get(url, default);
        return result.Result is OkObjectResult ok ? (LinkPreview)ok.Value! : null;
    }

    [Fact]
    public async Task Our_own_website_is_recognised_even_though_the_api_answers_elsewhere()
    {
        var factory = await SeedAsync();

        var preview = await PreviewAsync(
            factory, $"{SiteUrl}/o/nashville-paranormal/cases/printers-alley-knocking");

        Assert.NotNull(preview);
        Assert.Equal("Case", preview!.Kind);
        Assert.Equal("The Printers Alley Knocking", preview.Title);
        Assert.Contains("Nashville Paranormal", preview.Subtitle);
    }

    [Fact]
    public async Task The_old_case_reference_gets_a_card_too()
    {
        // "2026-042" is an address people share, and a link that opens perfectly well should not
        // be the one link that gets no card.
        var factory = await SeedAsync();

        var preview = await PreviewAsync(factory, "/o/nashville-paranormal/cases/2026-042");

        Assert.NotNull(preview);
        Assert.Equal("The Printers Alley Knocking", preview!.Title);
    }

    [Fact]
    public async Task A_domain_wearing_ours_is_a_stranger()
    {
        // The reason the host is compared whole: a Contains("ishaunted") test would take this.
        var factory = await SeedAsync();

        Assert.Null(await PreviewAsync(
            factory, "https://ishaunted.com.example.net/o/nashville-paranormal/cases/printers-alley-knocking"));
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("https://example.com/anything")]
    [InlineData("file:///etc/passwd")]
    public async Task Somebody_elses_address_is_never_looked_up(string url)
    {
        var factory = await SeedAsync();
        Assert.Null(await PreviewAsync(factory, url));
    }

    [Fact]
    public async Task A_group_gets_a_card_of_its_own()
    {
        var factory = await SeedAsync();

        var preview = await PreviewAsync(factory, "/o/nashville-paranormal");

        Assert.NotNull(preview);
        Assert.Equal("Group", preview!.Kind);
        Assert.Equal("Nashville Paranormal", preview.Title);
        // The kind, not the slug — the address is already in the link above the card.
        Assert.Equal("Investigation group", preview.Subtitle);
    }

    [Fact]
    public async Task An_address_of_ours_that_names_nothing_gets_no_card()
    {
        var factory = await SeedAsync();
        Assert.Null(await PreviewAsync(factory, "/o/nashville-paranormal/cases/no-such-case"));
    }

    // ── Another site's page, 2026-09-14 ───────────────────────────────────────────────────────────────────

    /// <summary>A single kept card for one address; asking it to fetch fails the test.</summary>
    private sealed class OneKept(string url, Ben.Data.Source.Entities.StoredLinkPreview row) : Ben.Data.WebApi.Services.LinkPreviews.ILinkPreviewService
    {
        public Task<Ben.Data.Source.Entities.StoredLinkPreview?> FindAsync(string asked, CancellationToken ct) =>
            Task.FromResult(asked == url ? row : null);

        public Task<Ben.Data.Source.Entities.StoredLinkPreview?> GetOrFetchAsync(string asked, Guid userId, bool refresh, CancellationToken ct) =>
            throw new InvalidOperationException("an anonymous preview must never fetch");
    }

    [Fact]
    public async Task Another_sites_page_gets_the_card_that_was_kept_and_nothing_is_fetched()
    {
        var factory = await SeedAsync();
        var id = Guid.NewGuid();
        var kept = new OneKept("https://findagrave.example/memorial/1", new Ben.Data.Source.Entities.StoredLinkPreview
        {
            Id = id, Url = "https://findagrave.example/memorial/1", UrlHash = "x", Domain = "findagrave.example",
            Title = "Rest Haven", Description = "Founded 1850.", SiteName = "Find a Grave", ThumbnailStoragePath = $"link-previews/{id}.jpg",
            Fetched = true, FetchedUtc = DateTime.UtcNow, ExpiresUtc = DateTime.UtcNow.AddDays(7),
        });

        var result = await Build(factory, kept).Get("https://findagrave.example/memorial/1", default);
        var card = Assert.IsType<LinkPreview>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Rest Haven", card.Title);
        Assert.Equal("findagrave.example", card.Domain);
        Assert.Equal($"/media/link-preview/{id}", card.ImageUrl);

        Assert.IsType<NotFoundResult>((await Build(factory, kept).Get("https://unknown.example/", default)).Result);
    }

    [Fact]
    public async Task A_kept_page_that_could_not_be_read_gets_no_card()
    {
        var factory = await SeedAsync();
        var kept = new OneKept("https://down.example/", new Ben.Data.Source.Entities.StoredLinkPreview
        {
            Id = Guid.NewGuid(), Url = "https://down.example/", UrlHash = "y", Domain = "down.example", Fetched = false,
            FetchedUtc = DateTime.UtcNow, ExpiresUtc = DateTime.UtcNow.AddDays(7),
        });
        Assert.IsType<NotFoundResult>((await Build(factory, kept).Get("https://down.example/", default)).Result);
    }

    /// <summary>No kept previews: these tests are about our own addresses.</summary>
    private sealed class NothingKept : Ben.Data.WebApi.Services.LinkPreviews.ILinkPreviewService
    {
        public Task<Ben.Data.Source.Entities.StoredLinkPreview?> FindAsync(string url, CancellationToken ct) =>
            Task.FromResult<Ben.Data.Source.Entities.StoredLinkPreview?>(null);

        public Task<Ben.Data.Source.Entities.StoredLinkPreview?> GetOrFetchAsync(string url, Guid userId, bool refresh, CancellationToken ct) =>
            throw new InvalidOperationException("an anonymous preview must never fetch");
    }

    // ── The card is a prose surface too (2026-09-17 audit) ───────────────────────────────────
    //
    // A published private-engagement case substitutes the client's real names everywhere else it
    // appears anonymously — the case page, discovery, the place page, the CMS embed. This card
    // did not, so the one word the case page would have replaced travelled in the preview instead.
    // The reason nothing caught it: PublicProseRedactionTests has a section per anonymous
    // controller and had none for this one.

    /// <summary>A published private-engagement case whose title names the client.</summary>
    private static async Task<IDbContextFactory<BenDataContext>> SeedPrivateEngagementAsync()
    {
        var factory = new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        await using var db = factory.CreateDbContext();
        var actor = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Nashville Paranormal", UrlName = "nashville-paranormal",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = actor,
        });
        db.Users.Add(new AppUser
        {
            Id = clientId, UserName = "vexley@t", Email = "vexley@t",
            FirstName = "Daniel", LastName = "Vexley", DisplayName = "Daniel Vexley",
            DateCreated = DateTime.UtcNow,
        });
        db.ClientRequests.Add(new ClientRequest
        {
            Id = requestId, AppUserId = clientId, Status = ClientRequestStatus.Assigned,
            StreetAddress1 = "1 Elm", City = "Nashville", State = "TN", ZipCode = "37201",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
        });
        db.Cases.Add(new Case
        {
            Id = Guid.NewGuid(), OrganizationId = orgId,
            Title = "The Vexley house", UrlName = "the-vexley-house",
            CaseYear = 2026, OrgCaseNumber = 43,
            City = "Nashville", State = "TN",
            IsPublic = true, Status = CaseStatus.Public,
            IsPrivateEngagement = true,
            ClientRequestId = requestId,
            PublicPseudonym = "The Hargrove Family",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = actor,
        });

        await db.SaveChangesAsync();
        return factory;
    }

    [Fact]
    public async Task A_private_engagement_cases_card_does_not_carry_the_clients_name()
    {
        var factory = await SeedPrivateEngagementAsync();

        var preview = await PreviewAsync(
            factory, "/o/nashville-paranormal/cases/the-vexley-house");

        Assert.NotNull(preview);
        Assert.DoesNotContain("Vexley", preview!.Title);
        // Substituted, not blanked — the card still has to say what it is pointing at.
        Assert.Contains("Hargrove", preview.Title);
    }

    [Fact]
    public async Task An_ordinary_cases_card_keeps_its_real_title()
    {
        var preview = await PreviewAsync(
            await SeedAsync(), "/o/nashville-paranormal/cases/printers-alley-knocking");

        Assert.Equal("The Printers Alley Knocking", preview!.Title);
    }
}
