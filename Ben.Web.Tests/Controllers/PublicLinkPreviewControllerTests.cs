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
    private static PublicLinkPreviewController Build(IDbContextFactory<BenDataContext> factory)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppBaseUrl"] = SiteUrl })
            .Build();

        return new PublicLinkPreviewController(factory, configuration)
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
}
