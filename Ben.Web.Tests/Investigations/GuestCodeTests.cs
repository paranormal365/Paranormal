using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Investigations;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Investigations;

/// <summary>
/// The guide's code and the guest's credential (item 248).
/// </summary>
/// <remarks>
/// The one this file exists for is <see cref="AGuestPassDoesNotOpenTheReadDoor"/>. Everything else
/// here is ordinary lifecycle cover; that test is the rule the feature is built around, and it was
/// written by first putting the guest door inside <c>MayContributeAsync</c> — the obvious
/// implementation — and watching it fail.
/// </remarks>
public sealed class GuestCodeTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid Lead = Guid.NewGuid();
    private static readonly Guid Guest = Guid.NewGuid();

    private static async Task<(SqliteTestDb Db, Guid InvestigationId)> SeedAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.Factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "Apple-Beta", DateCreated = DateTime.UtcNow, CreatedByAppUserId = Lead,
        });
        db.AppUsers.Add(new AppUser { Id = Lead, DisplayName = "The lead", DateCreated = DateTime.UtcNow });
        db.AppUsers.Add(new AppUser { Id = Guest, DisplayName = "A walk-up", DateCreated = DateTime.UtcNow });

        var investigationId = Guid.NewGuid();
        db.Investigations.Add(new Investigation
        {
            Id = investigationId, OrganizationId = OrgId, Title = "Franklin cemetery",
            ScheduledDateTime = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Lead,
        });

        await db.SaveChangesAsync();
        return (sqlite, investigationId);
    }

    private static async Task<InvestigationJoinCode> IssueAsync(SqliteTestDb sqlite, Guid investigationId)
    {
        await using var db = await sqlite.Factory.CreateDbContextAsync();
        var investigation = await db.Investigations.FirstAsync(i => i.Id == investigationId);
        var code = await GuestCodes.IssueAsync(db, investigation, Lead, DateTime.UtcNow.AddHours(8), default);
        await db.SaveChangesAsync();
        return code;
    }

    // ── the typed code ───────────────────────────────────────────────────────

    [Fact]
    public void ATypedCodeIsReadTheWayPeopleTypeIt()
    {
        var printed = GuestCodes.NewTypedCode();
        var bare = printed.Replace("-", "");

        // Lower case, no dash, and spaces where somebody paused — all the same code.
        Assert.Equal(printed, GuestCodes.NormaliseTyped(bare.ToLowerInvariant()));
        Assert.Equal(printed, GuestCodes.NormaliseTyped($"  {bare[..4]} {bare[4..]}  "));
        Assert.Equal(printed, GuestCodes.NormaliseTyped(printed));
    }

    [Fact]
    public void AStringThatIsNotACodeIsNotMistakenForOne()
    {
        Assert.Null(GuestCodes.NormaliseTyped(null));
        Assert.Null(GuestCodes.NormaliseTyped("   "));
        Assert.Null(GuestCodes.NormaliseTyped("ABC"));                 // too short
        Assert.Null(GuestCodes.NormaliseTyped("ABCD-EFGH-JKMN"));      // too long
        // A 64-character token must not be read as a typed code and looked up in the wrong column.
        Assert.Null(GuestCodes.NormaliseTyped(new string('A', 64)));
    }

    [Fact]
    public void TheAlphabetHasNothingAmbiguousInIt()
    {
        foreach (var confusable in "IL O0 1U".Replace(" ", ""))
            Assert.DoesNotContain(confusable, GuestCodes.TypedAlphabet);
    }

    // ── the code's life ──────────────────────────────────────────────────────

    [Fact]
    public async Task IssuingAgainRetiresTheOldSheetButNotTheEveningsWork()
    {
        var (sqlite, investigationId) = await SeedAsync();
        await using var _ = sqlite;

        var first = await IssueAsync(sqlite, investigationId);

        await using (var db = await sqlite.Factory.CreateDbContextAsync())
        {
            var code = await db.InvestigationJoinCodes.FirstAsync(c => c.Id == first.Id);
            var (pass, refused) = await GuestCodes.RedeemAsync(db, code, Guest, "A walk-up", default);
            Assert.Null(refused);
            Assert.NotNull(pass);
            await db.SaveChangesAsync();
        }

        await IssueAsync(sqlite, investigationId);

        await using (var db = await sqlite.Factory.CreateDbContextAsync())
        {
            var live = await GuestCodes.LiveForAsync(db, investigationId, default);
            Assert.Single(live);
            Assert.NotEqual(first.Id, live[0].Id);

            var retired = await db.InvestigationJoinCodes.FirstAsync(c => c.Id == first.Id);
            Assert.NotNull(retired.RevokedUtc);

            // The guest who scanned the old sheet is standing in the building. Reprinting must
            // not delete their evening — which is what revoking their pass here would do.
            Assert.False(await GuestCodes.HoldsALivePassAsync(db, investigationId, Guest, default));
        }
    }

    [Fact]
    public async Task RevokingTheCodeClosesEverybodysPassAtOnce()
    {
        var (sqlite, investigationId) = await SeedAsync();
        await using var _ = sqlite;

        var code = await IssueAsync(sqlite, investigationId);

        await using (var db = await sqlite.Factory.CreateDbContextAsync())
        {
            var live = await db.InvestigationJoinCodes.FirstAsync(c => c.Id == code.Id);
            await GuestCodes.RedeemAsync(db, live, Guest, null, default);
            await db.SaveChangesAsync();
            Assert.True(await GuestCodes.HoldsALivePassAsync(db, investigationId, Guest, default));
        }

        await using (var db = await sqlite.Factory.CreateDbContextAsync())
        {
            foreach (var c in await GuestCodes.LiveForAsync(db, investigationId, default))
                GuestCodes.Revoke(c, Lead);
            await db.SaveChangesAsync();

            Assert.False(await GuestCodes.HoldsALivePassAsync(db, investigationId, Guest, default));
        }
    }

    [Fact]
    public async Task AnExpiredCodeAdmitsNobodyAndSaysWhy()
    {
        var (sqlite, investigationId) = await SeedAsync();
        await using var _ = sqlite;

        var code = await IssueAsync(sqlite, investigationId);

        await using var db = await sqlite.Factory.CreateDbContextAsync();
        var live = await db.InvestigationJoinCodes.FirstAsync(c => c.Id == code.Id);
        await GuestCodes.RedeemAsync(db, live, Guest, null, default);
        live.ExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        Assert.False(await GuestCodes.HoldsALivePassAsync(db, investigationId, Guest, default));
        Assert.Contains("run out", GuestCodes.WhyThisCodeIsRefused(live));
    }

    [Fact]
    public async Task ACodeCannotOutliveItsNight()
    {
        var (sqlite, investigationId) = await SeedAsync();
        await using var _ = sqlite;

        await using var db = await sqlite.Factory.CreateDbContextAsync();
        var investigation = await db.Investigations.FirstAsync(i => i.Id == investigationId);

        // Asking for a year gets a day. A sheet that still opens something in March is the whole
        // failure this is here to prevent, and asking politely must not be a way round it.
        var code = await GuestCodes.IssueAsync(db, investigation, Lead, DateTime.UtcNow.AddYears(1), default);
        Assert.True(code.ExpiresUtc <= DateTime.UtcNow + GuestCodes.LongestLife + TimeSpan.FromSeconds(5));
    }

    // ── the credential ───────────────────────────────────────────────────────

    [Fact]
    public async Task ScanningTwiceIsOneCredential()
    {
        var (sqlite, investigationId) = await SeedAsync();
        await using var _ = sqlite;

        var code = await IssueAsync(sqlite, investigationId);

        await using var db = await sqlite.Factory.CreateDbContextAsync();
        var live = await db.InvestigationJoinCodes.FirstAsync(c => c.Id == code.Id);

        var (first, _) = await GuestCodes.RedeemAsync(db, live, Guest, "A walk-up", default);
        await db.SaveChangesAsync();
        var (again, _) = await GuestCodes.RedeemAsync(db, live, Guest, "A walk-up", default);
        await db.SaveChangesAsync();

        Assert.Equal(first!.Id, again!.Id);
        Assert.Equal(1, await db.InvestigationGuestPasses.CountAsync(p => p.AppUserId == Guest));
    }

    [Fact]
    public async Task ARevokedHolderCannotScanTheirWayBackIn()
    {
        var (sqlite, investigationId) = await SeedAsync();
        await using var _ = sqlite;

        var code = await IssueAsync(sqlite, investigationId);

        await using var db = await sqlite.Factory.CreateDbContextAsync();
        var live = await db.InvestigationJoinCodes.FirstAsync(c => c.Id == code.Id);

        var (pass, _) = await GuestCodes.RedeemAsync(db, live, Guest, null, default);
        await db.SaveChangesAsync();

        GuestCodes.RevokePass(pass!, Lead);
        await db.SaveChangesAsync();

        // The sheet is still on the table. Handing the pass back to whoever scans again would
        // make revoking one person mean nothing at all.
        var (again, refused) = await GuestCodes.RedeemAsync(db, live, Guest, null, default);
        Assert.Null(again);
        Assert.Contains("took that pass back", refused);
        Assert.False(await GuestCodes.HoldsALivePassAsync(db, investigationId, Guest, default));
    }

    [Fact]
    public async Task RevokingOneHolderLeavesTheOthersWorking()
    {
        var (sqlite, investigationId) = await SeedAsync();
        await using var _ = sqlite;

        var other = Guid.NewGuid();
        var code = await IssueAsync(sqlite, investigationId);

        await using var db = await sqlite.Factory.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser { Id = other, DisplayName = "Another guest", DateCreated = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var live = await db.InvestigationJoinCodes.FirstAsync(c => c.Id == code.Id);
        var (mine, _) = await GuestCodes.RedeemAsync(db, live, Guest, null, default);
        await GuestCodes.RedeemAsync(db, live, other, null, default);
        await db.SaveChangesAsync();

        GuestCodes.RevokePass(mine!, Lead);
        await db.SaveChangesAsync();

        Assert.False(await GuestCodes.HoldsALivePassAsync(db, investigationId, Guest, default));
        Assert.True(await GuestCodes.HoldsALivePassAsync(db, investigationId, other, default));
    }
}
