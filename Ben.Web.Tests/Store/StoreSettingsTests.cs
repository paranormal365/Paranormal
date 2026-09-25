using System.Security.Claims;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Ben.Web.Website.Library.SuperAdmin;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The store's settings read back typed, with their defaults, whatever is in the table
/// (storefront S1.1).
/// </summary>
public sealed class StoreSettingsReaderTests
{
    private static async Task<StoreSettingsSnapshot> ReadWithAsync(params (string Key, string? Value)[] rows)
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var admin = StoreTestData.Person(db);
        foreach (var (key, value) in rows)
            db.SiteSettings.Add(new SiteSetting
            {
                Id = Guid.NewGuid(), Key = key, Value = value,
                DateCreated = StoreTestData.Now, CreatedByAppUserId = admin.Id,
            });
        await db.SaveChangesAsync();
        return await StoreSettingsReader.ReadAsync(db);
    }

    [Fact]
    public async Task An_untouched_site_reads_the_defaults()
    {
        var s = await ReadWithAsync();

        Assert.False(s.Enabled);
        Assert.True(s.CheckoutEnabled);
        Assert.Equal((0m, 0m), (s.ShippingFlatRate, s.FreeShippingThreshold));
        Assert.Equal((3, 30, 15), (s.LowStockThreshold, s.ReturnsWindowDays, s.ReservationMinutes));
        Assert.False(s.HasShipFrom);
        Assert.Null(s.SupportEmail);
        Assert.False(s.LinkEnabled);
    }

    [Fact]
    public async Task What_the_settings_page_stores_is_what_the_store_reads()
    {
        (string, string?) Checked(string key, string raw)
        {
            var (value, refusal) = StoreSettingsValidation.Check(key, raw);
            Assert.Null(refusal);
            return (key, value);
        }

        var s = await ReadWithAsync(
            (SiteSettingKeys.FeatureStore, "true"),
            Checked(SiteSettingKeys.StoreCheckoutEnabled, "False"),
            Checked(SiteSettingKeys.StoreShippingFlatRateUsd, " 7.5 "),
            Checked(SiteSettingKeys.StoreFreeShippingThresholdUsd, "100"),
            Checked(SiteSettingKeys.StoreLowStockThreshold, "5"),
            Checked(SiteSettingKeys.StoreShipFromStreet, "13 Crossroads Lane"),
            Checked(SiteSettingKeys.StoreShipFromCity, "Adams"),
            Checked(SiteSettingKeys.StoreShipFromState, "tn"),
            Checked(SiteSettingKeys.StoreShipFromZip, "37010"),
            Checked(SiteSettingKeys.StoreSupportEmail, "shop@ishaunted.com"),
            Checked(SiteSettingKeys.StoreReturnsWindowDays, "14"),
            Checked(SiteSettingKeys.StoreReservationMinutes, "20"),
            Checked(SiteSettingKeys.StoreLinkEnabled, "true"));

        Assert.Equal(
            new StoreSettingsSnapshot(true, false, 7.50m, 100m, 5,
                new StoreShipFrom("13 Crossroads Lane", "Adams", "TN", "37010"), true,
                "shop@ishaunted.com", 14, 20, true),
            s);
    }

    /// <summary>Only a value typed straight into the table can be nonsense; it reads as unset.</summary>
    [Theory]
    [InlineData("-5")]
    [InlineData("abc")]
    public async Task A_nonsense_shipping_rate_reads_as_nothing_charged(string raw)
    {
        var s = await ReadWithAsync(
            (SiteSettingKeys.StoreShippingFlatRateUsd, raw),
            (SiteSettingKeys.StoreFreeShippingThresholdUsd, raw),
            (SiteSettingKeys.StoreReservationMinutes, raw));

        Assert.Equal((0m, 0m, 15), (s.ShippingFlatRate, s.FreeShippingThreshold, s.ReservationMinutes));
    }

    [Fact]
    public async Task A_ship_from_address_counts_only_with_all_four_lines()
    {
        var three = await ReadWithAsync(
            (SiteSettingKeys.StoreShipFromStreet, "13 Crossroads Lane"),
            (SiteSettingKeys.StoreShipFromCity, "Adams"),
            (SiteSettingKeys.StoreShipFromZip, "37010"));
        Assert.False(three.HasShipFrom);

        var badState = await ReadWithAsync(
            (SiteSettingKeys.StoreShipFromStreet, "13 Crossroads Lane"),
            (SiteSettingKeys.StoreShipFromCity, "Adams"),
            (SiteSettingKeys.StoreShipFromState, "Tennessee"),
            (SiteSettingKeys.StoreShipFromZip, "37010"));
        Assert.False(badState.HasShipFrom);
    }
}

/// <summary>Every refusal the store settings page can give, word for word (storefront S1.1).</summary>
public sealed class StoreSettingsValidationTests
{
    [Theory]
    [InlineData(SiteSettingKeys.StoreShippingFlatRateUsd, "-1", "Shipping can't be negative.")]
    [InlineData(SiteSettingKeys.StoreShippingFlatRateUsd, "seven", "Shipping is an amount in dollars, like 7.50.")]
    [InlineData(SiteSettingKeys.StoreShippingFlatRateUsd, "7.505", "Shipping is dollars and cents — two decimal places at most.")]
    [InlineData(SiteSettingKeys.StoreShippingFlatRateUsd, "10000.01", "Shipping is $10,000.00 at most.")]
    [InlineData(SiteSettingKeys.StoreFreeShippingThresholdUsd, "-0.01", "The free-shipping threshold can't be negative.")]
    [InlineData(SiteSettingKeys.StoreLowStockThreshold, "101", "Low-stock warning is 0 to 100.")]
    [InlineData(SiteSettingKeys.StoreLowStockThreshold, "2.5", "Low-stock warning is 0 to 100.")]
    [InlineData(SiteSettingKeys.StoreReturnsWindowDays, "366", "Returns window is 0 to 365 days.")]
    [InlineData(SiteSettingKeys.StoreReservationMinutes, "4", "Reservation is 5 to 240 minutes.")]
    [InlineData(SiteSettingKeys.StoreReservationMinutes, "241", "Reservation is 5 to 240 minutes.")]
    [InlineData(SiteSettingKeys.StoreShipFromState, "Tennessee", "Ship-from state is two letters, like TN.")]
    [InlineData(SiteSettingKeys.StoreShipFromState, "XX", "Ship-from state is two letters, like TN.")]
    [InlineData(SiteSettingKeys.StoreShipFromZip, "3701", "A ZIP code is five digits, or ZIP+4 like 37201-1234.")]
    [InlineData(SiteSettingKeys.StoreSupportEmail, "shop at ishaunted", "That doesn't look like an email address.")]
    [InlineData(SiteSettingKeys.StoreSupportEmail, "Shop <shop@ishaunted.com>", "That doesn't look like an email address.")]
    [InlineData(SiteSettingKeys.StoreCheckoutEnabled, "yes", "Take orders is on or off.")]
    [InlineData(SiteSettingKeys.FeatureStore, "true", "'features.store' is not a store setting.")]
    public void A_bad_value_is_refused_with_its_sentence(string key, string raw, string sentence)
        => Assert.Equal((null, sentence), StoreSettingsValidation.Check(key, raw));

    [Theory]
    [InlineData(SiteSettingKeys.StoreShippingFlatRateUsd, "7.5", "7.50")]
    [InlineData(SiteSettingKeys.StoreShippingFlatRateUsd, "0", "0.00")]
    [InlineData(SiteSettingKeys.StoreShipFromState, " tn ", "TN")]
    [InlineData(SiteSettingKeys.StoreShipFromZip, "37201-1234", "37201-1234")]
    [InlineData(SiteSettingKeys.StoreLowStockThreshold, "0", "0")]
    [InlineData(SiteSettingKeys.StoreLinkEnabled, "True", "true")]
    public void A_good_value_is_stored_in_one_spelling(string key, string raw, string stored)
        => Assert.Equal((stored, null), StoreSettingsValidation.Check(key, raw));

    [Fact]
    public void Clearing_any_store_setting_is_allowed()
    {
        foreach (var key in SiteSettingKeys.StoreKeys)
            Assert.Equal((null, null), StoreSettingsValidation.Check(key, "  "));
    }

    [Fact]
    public void Every_store_key_is_declared_filed_under_Store_and_checked()
    {
        Assert.Equal(15, SiteSettingKeys.StoreKeys.Length);   // store sellers P9 added the markup and the two fee settings
        Assert.Equal(SiteSettingKeys.StoreKeys,
            SiteSettingKeys.Groups.Single(g => g.Name == SiteSettingKeys.StoreGroupName).Keys);
        Assert.All(SiteSettingKeys.StoreKeys, key =>
        {
            Assert.StartsWith(SiteSettingKeys.StorePrefix, key);
            Assert.True(SiteSettingsService.IsKnownKey(key), key);
            Assert.DoesNotContain("is not a store setting", StoreSettingsValidation.Check(key, "x").Refusal ?? "");
        });
        Assert.Contains(SiteSettingKeys.StoreCheckoutEnabled, SiteSettingKeys.BooleanKeys);
        Assert.Contains(SiteSettingKeys.StoreLinkEnabled, SiteSettingKeys.BooleanKeys);
    }
}

/// <summary>
/// The generic site-settings door refuses store keys, so nothing reaches the store unchecked
/// (storefront S1.1).
/// </summary>
public sealed class AdminSiteSettingControllerTests
{
    private static AdminSiteSettingController Controller(SqliteTestDb sqlite, Guid admin) => new(
        new SiteSettingsService(sqlite.Factory), new Mock<IAuditLogService>().Object)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, admin.ToString())], "Bearer")),
            },
        },
    };

    private static async Task<(SqliteTestDb, Guid)> SiteWithAnAdminAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var admin = StoreTestData.Person(db);
        await db.SaveChangesAsync();
        return (sqlite, admin.Id);
    }

    [Fact]
    public async Task A_store_setting_sent_here_is_refused_and_nothing_is_saved()
    {
        var (sqlite, admin) = await SiteWithAnAdminAsync();
        await using var _ = sqlite;

        var result = await Controller(sqlite, admin).Set(
            SiteSettingKeys.StoreShipFromState, new SetSiteSettingRequest("Tennessee"), default);

        var refused = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Store settings are edited on the store settings page.", refused.Value);
        await using var check = await sqlite.NewContextAsync();
        Assert.False(await check.SiteSettings.AnyAsync(s => s.Key.StartsWith("store.")));
    }

    /// <summary>The refusal is aimed: the shop's own switch lives under Features and still saves here.</summary>
    [Fact]
    public async Task The_store_switch_itself_still_saves_here()
    {
        var (sqlite, admin) = await SiteWithAnAdminAsync();
        await using var _ = sqlite;

        var result = await Controller(sqlite, admin).Set(
            SiteSettingKeys.FeatureStore, new SetSiteSettingRequest("true"), default);

        Assert.IsType<OkObjectResult>(result.Result);
        await using var check = await sqlite.NewContextAsync();
        Assert.Equal("true", (await check.SiteSettings.SingleAsync(s => s.Key == SiteSettingKeys.FeatureStore)).Value);
    }

    [Fact]
    public async Task Take_orders_reads_as_on_until_somebody_pauses_it()
    {
        var (sqlite, admin) = await SiteWithAnAdminAsync();
        await using var _ = sqlite;

        var all = Assert.IsType<OkObjectResult>((await Controller(sqlite, admin).GetAll(default)).Result);
        var takeOrders = ((IEnumerable<SiteSettingRecord>)all.Value!).Single(s => s.Key == SiteSettingKeys.StoreCheckoutEnabled);

        Assert.True(takeOrders.IsBoolean);
        Assert.True(takeOrders.DefaultWhenUnset);
        Assert.Equal(SiteSettingKeys.StoreGroupName, takeOrders.Group);
    }
}

/// <summary>
/// On Site Settings the Store section is listed, not edited, and points at the page that edits it
/// (storefront S1.1).
/// </summary>
/// <remarks>
/// The component is rendered rather than the whole page: the page loads its settings only once
/// the circuit is interactive, which <see cref="HtmlRenderer"/> never is, so a page render could
/// only ever see the loading state. <see cref="The_page_hands_the_Store_section_to_it"/> pins the
/// one line that joins the two.
/// </remarks>
public sealed class StoreSettingsReadOnlyTests
{
    private static SiteSettingRecord Row(string key, string? value, bool isBoolean = false, bool defaultOn = false)
        => new(key, SiteSettingsService.LabelFor(key), value, "d", StoreTestData.Now,
               IsBoolean: isBoolean, DefaultWhenUnset: defaultOn, Group: SiteSettingKeys.StoreGroupName);

    private static async Task<string> RenderAsync(IReadOnlyList<SiteSettingRecord> rows)
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<StoreSettingsReadOnly>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(StoreSettingsReadOnly.Settings)] = rows }));
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    [Fact]
    public async Task The_store_section_has_no_inputs_and_one_way_to_the_store_settings_page()
    {
        var html = await RenderAsync([
            Row(SiteSettingKeys.StoreCheckoutEnabled, null, isBoolean: true, defaultOn: true),
            Row(SiteSettingKeys.StoreShipFromState, "TN"),
            Row(SiteSettingKeys.StoreShippingFlatRateUsd, null),
        ]);

        Assert.DoesNotContain("<input", html);
        Assert.DoesNotContain("<textarea", html);
        Assert.DoesNotContain("<button", html);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<a "));
        Assert.Contains("href=\"/admin/store/settings\"", html);
        Assert.Contains("On (default)", html);
        Assert.Contains(">TN<", html);
        Assert.Contains("Not set — the default applies.", html);
    }

    [Fact]
    public void The_page_hands_the_Store_section_to_it()
    {
        Assert.Equal(SiteSettingKeys.StoreGroupName, StoreSettingsReadOnly.GroupName);
        Assert.True(AdminSiteSettings.IsEditedElsewhere(SiteSettingKeys.StoreGroupName));
        Assert.All(SiteSettingKeys.Groups.Where(g => g.Name != SiteSettingKeys.StoreGroupName),
            g => Assert.False(AdminSiteSettings.IsEditedElsewhere(g.Name), g.Name));
    }
}
