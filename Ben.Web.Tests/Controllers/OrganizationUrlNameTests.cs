using Ben.Data.Common;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The address an organization lives at: what it may be, who may hold it, and what happens to the
/// old one (backlog item #89).
/// </summary>
/// <remarks>
/// <para>Three separate faults, all on the one column people actually type.</para>
///
/// <para><b>Anything at all was accepted</b> — spaces, slashes, punctuation — because both write
/// paths trimmed and lowercased and did nothing else.</para>
///
/// <para><b>Uniqueness was checked on create and not on rename</b>, with no index behind either. Two
/// organizations could hold one address, and every one of the seventeen lookup sites is a
/// first-match query, so <c>/o/ghost-squad</c> served whichever row came back first. A group could
/// rename onto another group's address and take their traffic.</para>
///
/// <para><b>A rename broke every link ever shared.</b> An organization's address is the one part of
/// this product that ends up on a business card, and it simply stopped resolving.</para>
/// </remarks>
public sealed class OrganizationUrlNameTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid OrgId)> SeedAsync(
        string urlName = "ghost-squad")
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();

        var id = Guid.NewGuid();
        db.Organizations.Add(new Organization
        {
            Id = id, Name = "Ghost Squad", UrlName = urlName, DateCreated = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
        return (factory, id);
    }

    // ── The shape ────────────────────────────────────────────────────────────

    /// <summary>
    /// The positive case first: ordinary addresses people would actually choose are accepted.
    /// A rule that rejected these would be worse than the missing rule it replaced.
    /// </summary>
    [Theory]
    [InlineData("ghost-squad")]
    [InlineData("paranormal365")]
    [InlineData("the-society-for-psychical-research")]
    [InlineData("team-2")]
    [InlineData("ab")]
    public void An_ordinary_address_is_allowed(string urlName)
        => Assert.Null(UrlNameRules.RefusalFor(urlName));

    /// <summary>
    /// Case and surrounding space are normalized rather than refused — somebody pasting
    /// " Ghost-Squad " meant "ghost-squad", and saying no to that would be pedantry.
    /// </summary>
    [Theory]
    [InlineData(" Ghost-Squad ")]
    [InlineData("GHOSTSQUAD")]
    public void Case_and_space_are_normalized_not_refused(string urlName)
        => Assert.Null(UrlNameRules.RefusalFor(urlName));

    [Theory]
    [InlineData("ghost squad")]      // a space breaks the address
    [InlineData("ghost/squad")]      // a slash invents a path segment
    [InlineData("../admin")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("double--hyphen")]
    [InlineData("emoji-👻")]
    [InlineData("a")]                // too short to be meaningful
    [InlineData("")]
    [InlineData(null)]
    public void A_malformed_address_is_refused(string? urlName)
        => Assert.NotNull(UrlNameRules.RefusalFor(urlName));

    /// <summary>The refusal says what is wrong, because every one of these is a fixable typo.</summary>
    [Fact]
    public void The_refusal_explains_itself()
    {
        var refusal = UrlNameRules.RefusalFor("ghost squad");

        Assert.NotNull(refusal);
        Assert.Contains("hyphen", refusal!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_word_the_site_uses_itself_is_refused_with_a_suggestion()
    {
        var refusal = UrlNameRules.RefusalFor("admin");

        Assert.NotNull(refusal);
        Assert.Contains("admin-team", refusal!);
    }

    // ── Uniqueness ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_free_address_is_available()
    {
        var (factory, _) = await SeedAsync();
        await using var db = await factory.CreateDbContextAsync();

        Assert.Null(await OrganizationUrlNames.RefusalForAsync(db, "spectre-club", null, default));
    }

    [Fact]
    public async Task Another_organizations_address_is_refused()
    {
        var (factory, _) = await SeedAsync();
        await using var db = await factory.CreateDbContextAsync();

        Assert.NotNull(await OrganizationUrlNames.RefusalForAsync(db, "ghost-squad", null, default));
    }

    /// <summary>
    /// Differing only in case is still the same address, whatever the database's collation says.
    /// </summary>
    [Fact]
    public async Task The_same_address_in_a_different_case_is_refused()
    {
        var (factory, _) = await SeedAsync();
        await using var db = await factory.CreateDbContextAsync();

        Assert.NotNull(await OrganizationUrlNames.RefusalForAsync(db, "Ghost-Squad", null, default));
    }

    /// <summary>
    /// Re-saving the settings form without touching the address is not a collision with yourself.
    /// Without this the whole page would become unsaveable.
    /// </summary>
    [Fact]
    public async Task Keeping_your_own_address_is_not_a_collision()
    {
        var (factory, orgId) = await SeedAsync();
        await using var db = await factory.CreateDbContextAsync();

        Assert.Null(await OrganizationUrlNames.RefusalForAsync(db, "ghost-squad", orgId, default));
    }

    // ── Renaming, and the alias it leaves behind ─────────────────────────────

    /// <summary>
    /// The heart of it: after a rename, the old address still finds the organization.
    /// </summary>
    [Fact]
    public async Task An_old_address_still_resolves_after_a_rename()
    {
        var (factory, orgId) = await SeedAsync();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var org = await db.Organizations.FirstAsync(o => o.Id == orgId);
            await OrganizationUrlNames.ApplyAsync(db, org, "spectre-club", UserId, default);
            await db.SaveChangesAsync();
        }

        await using var check = await factory.CreateDbContextAsync();

        var (viaOld, wasAlias) = await OrganizationUrlNames.ResolveAsync(check, "ghost-squad", default);
        Assert.Equal(orgId, viaOld?.Id);
        Assert.True(wasAlias, "The caller must be told it arrived on a retired address so it can redirect.");

        var (viaNew, wasCurrent) = await OrganizationUrlNames.ResolveAsync(check, "spectre-club", default);
        Assert.Equal(orgId, viaNew?.Id);
        Assert.False(wasCurrent);
    }

    /// <summary>
    /// A retired address stays with the group that used it. Handing it to somebody else would point
    /// a saved link at strangers, which is worse than the link being dead.
    /// </summary>
    [Fact]
    public async Task A_retired_address_cannot_be_taken_by_another_organization()
    {
        var (factory, orgId) = await SeedAsync();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var org = await db.Organizations.FirstAsync(o => o.Id == orgId);
            await OrganizationUrlNames.ApplyAsync(db, org, "spectre-club", UserId, default);
            await db.SaveChangesAsync();
        }

        await using var check = await factory.CreateDbContextAsync();
        var refusal = await OrganizationUrlNames.RefusalForAsync(check, "ghost-squad", null, default);

        Assert.NotNull(refusal);
        Assert.Contains("links", refusal!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>But the group that retired it may take it back.</summary>
    [Fact]
    public async Task The_original_holder_may_reclaim_its_old_address()
    {
        var (factory, orgId) = await SeedAsync();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var org = await db.Organizations.FirstAsync(o => o.Id == orgId);
            await OrganizationUrlNames.ApplyAsync(db, org, "spectre-club", UserId, default);
            await db.SaveChangesAsync();
        }

        await using var db2 = await factory.CreateDbContextAsync();
        Assert.Null(await OrganizationUrlNames.RefusalForAsync(db2, "ghost-squad", orgId, default));
    }

    /// <summary>
    /// Renaming away and back leaves the group holding one alias, not two identical ones — which the
    /// unique index would refuse outright.
    /// </summary>
    [Fact]
    public async Task Renaming_back_and_forth_does_not_duplicate_the_alias()
    {
        var (factory, orgId) = await SeedAsync();

        foreach (var name in new[] { "spectre-club", "ghost-squad", "spectre-club" })
        {
            await using var db = await factory.CreateDbContextAsync();
            var org = await db.Organizations.FirstAsync(o => o.Id == orgId);
            await OrganizationUrlNames.ApplyAsync(db, org, name, UserId, default);
            await db.SaveChangesAsync();
        }

        await using var check = await factory.CreateDbContextAsync();
        var aliases = await check.OrganizationUrlNameAliases.ToListAsync();

        Assert.Equal(aliases.Select(a => a.UrlName).Distinct().Count(), aliases.Count);
    }

    /// <summary>Saving the same address again writes no alias — nothing was retired.</summary>
    [Fact]
    public async Task Saving_an_unchanged_address_records_nothing()
    {
        var (factory, orgId) = await SeedAsync();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var org = await db.Organizations.FirstAsync(o => o.Id == orgId);
            await OrganizationUrlNames.ApplyAsync(db, org, "ghost-squad", UserId, default);
            await db.SaveChangesAsync();
        }

        await using var check = await factory.CreateDbContextAsync();
        Assert.Empty(await check.OrganizationUrlNameAliases.ToListAsync());
    }

    /// <summary>An address nobody has ever held resolves to nothing, rather than to something.</summary>
    [Fact]
    public async Task An_unknown_address_resolves_to_nothing()
    {
        var (factory, _) = await SeedAsync();
        await using var db = await factory.CreateDbContextAsync();

        var (org, viaAlias) = await OrganizationUrlNames.ResolveAsync(db, "never-existed", default);

        Assert.Null(org);
        Assert.False(viaAlias);
    }
}
