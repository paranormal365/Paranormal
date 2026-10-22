using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Every declared setting can be saved for the first time on a fresh database.
/// </summary>
/// <remarks>
/// The first save of a setting writes its row, description and all, and the description column is
/// <see cref="SiteSetting.DescriptionMaxLength"/> wide. The Ghost Walk switch's explanation was longer,
/// so turning it on answered "Something you typed is longer than it can be" and the subscription could
/// never be switched on (full e2e run on a fresh database, 10/21/2026). The in-memory provider does not
/// enforce widths, so this reads the width from the real model and checks what was written.
/// </remarks>
public sealed class SiteSettingDescriptionFitsTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    [Fact]
    public async Task The_first_save_of_every_setting_writes_a_description_that_fits_its_column()
    {
        var factory = new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new SiteSettingsService(factory);

        await using (var model = await factory.CreateDbContextAsync())
        {
            var width = model.Model.FindEntityType(typeof(SiteSetting))!
                .FindProperty(nameof(SiteSetting.Description))!.GetMaxLength();
            Assert.Equal(SiteSetting.DescriptionMaxLength, width);
        }

        var tooLong = new List<string>();
        foreach (var (key, _, _) in SiteSettingKeys.Seed)
        {
            var row = await service.SetAsync(key, null, Guid.NewGuid());
            if (row.Description is { Length: > SiteSetting.DescriptionMaxLength })
                tooLong.Add($"{key}: {row.Description.Length} characters");
        }
        Assert.Empty(tooLong);
    }

    [Fact]
    public async Task The_admin_page_still_reads_the_whole_explanation()
    {
        var factory = new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new SiteSettingsService(factory);

        await service.SetAsync(SiteSettingKeys.GhostWalkEnabled, "true", Guid.NewGuid());

        var declared = SiteSettingKeys.Seed.First(s => s.Key == SiteSettingKeys.GhostWalkEnabled).Description;
        var shown = (await service.GetAllAsync()).First(s => s.Key == SiteSettingKeys.GhostWalkEnabled).Description;
        Assert.Equal(declared, shown);
    }
}
