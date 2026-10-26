using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// A person's own named EVP scan settings (phase 5, 10/26/2026).
/// </summary>
public class EvpScanPresetControllerTests
{
    private static readonly EvpDetectionOptions Basement =
        EvpDetectionOptions.FromSensitivity(EvpSensitivity.High) with { BandLowHz = 150, FloorWindowSeconds = 20 };

    private static IDbContextFactory<BenDataContext> CreateFactory() =>
        new PooledDbContextFactory<BenDataContext>(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static EvpScanPresetController As(IDbContextFactory<BenDataContext> factory, Guid userId) => new(factory)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
            },
        },
    };

    private static async Task<EvpScanPresetRecord> CreateAsync(
        IDbContextFactory<BenDataContext> factory, Guid userId, string name, EvpDetectionOptions options, bool isDefault = false)
    {
        var result = await As(factory, userId).Create(new SaveEvpScanPresetRequest(name, options, isDefault), default);
        return Assert.IsType<EvpScanPresetRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    private static async Task<IReadOnlyList<EvpScanPresetRecord>> MineAsync(IDbContextFactory<BenDataContext> factory, Guid userId)
        => Assert.IsAssignableFrom<IReadOnlyList<EvpScanPresetRecord>>(
            Assert.IsType<OkObjectResult>((await As(factory, userId).Mine(default)).Result).Value);

    [Fact]
    public async Task A_saved_preset_comes_back_with_every_setting()
    {
        var factory = CreateFactory();
        var me = Guid.NewGuid();

        await CreateAsync(factory, me, "  Basement  ", Basement);

        var mine = await MineAsync(factory, me);
        var saved = Assert.Single(mine);
        Assert.Equal("Basement", saved.Name);
        Assert.Equal(Basement, saved.Options);
        Assert.Null(saved.Problem);
    }

    [Fact]
    public async Task Nobody_sees_anybody_elses_presets()
    {
        var factory = CreateFactory();
        var mine = await CreateAsync(factory, Guid.NewGuid(), "Basement", Basement);
        var stranger = Guid.NewGuid();

        Assert.Empty(await MineAsync(factory, stranger));
        Assert.IsType<NotFoundObjectResult>((await As(factory, stranger)
            .Update(mine.Id, new SaveEvpScanPresetRequest("Mine now", Basement), default)).Result);
        Assert.IsType<NotFoundObjectResult>(await As(factory, stranger).Delete(mine.Id, default));
    }

    [Fact]
    public async Task Two_presets_cannot_share_a_name()
    {
        var factory = CreateFactory();
        var me = Guid.NewGuid();
        await CreateAsync(factory, me, "Basement", Basement);

        var again = await As(factory, me).Create(new SaveEvpScanPresetRequest("Basement", Basement), default);

        var conflict = Assert.IsType<ConflictObjectResult>(again.Result);
        Assert.Contains("already have a preset called", conflict.Value?.ToString());
    }

    [Fact]
    public async Task Settings_the_scan_would_refuse_are_never_saved()
    {
        var factory = CreateFactory();

        var result = await As(factory, Guid.NewGuid()).Create(
            new SaveEvpScanPresetRequest("Upside down", new EvpDetectionOptions(BandLowHz: 3000, BandHighHz: 300)), default);

        Assert.Contains("listening range", Assert.IsType<BadRequestObjectResult>(result.Result).Value?.ToString());
    }

    [Fact]
    public async Task Only_one_preset_is_the_default()
    {
        var factory = CreateFactory();
        var me = Guid.NewGuid();
        var first  = await CreateAsync(factory, me, "Basement", Basement, isDefault: true);
        var second = await CreateAsync(factory, me, "Attic", Basement with { ThresholdDb = 8 }, isDefault: true);

        var mine = await MineAsync(factory, me);
        Assert.Equal([second.Id], mine.Where(p => p.IsDefault).Select(p => p.Id).ToArray());
        Assert.False(mine.Single(p => p.Id == first.Id).IsDefault);
    }

    /// <summary>A preset saved before a range tightened says why it can't be used instead of running.</summary>
    [Fact]
    public async Task A_stored_preset_the_scan_would_now_refuse_says_why()
    {
        var factory = CreateFactory();
        var me = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.EvpScanPresets.Add(new EvpScanPreset
            {
                Id = Guid.NewGuid(), AppUserId = me, Name = "Old", DateCreated = DateTime.UtcNow,
                OptionsJson = """{"thresholdDb":40}""",
            });
            await db.SaveChangesAsync();
        }

        var old = Assert.Single(await MineAsync(factory, me));

        Assert.Contains("Threshold", old.Problem);
    }

    [Fact]
    public async Task A_deleted_preset_is_gone()
    {
        var factory = CreateFactory();
        var me = Guid.NewGuid();
        var preset = await CreateAsync(factory, me, "Basement", Basement);

        Assert.IsType<NoContentResult>(await As(factory, me).Delete(preset.Id, default));
        Assert.Empty(await MineAsync(factory, me));
    }
}
