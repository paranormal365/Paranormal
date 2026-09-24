using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The store's admin API, for arranging what a browser test needs (storefront): a shelf, a product
/// with a picture, two sizes, live or not — each with a name nobody else uses.
/// </summary>
internal sealed class StoreTestApi(HttpClient http) : IDisposable
{
    public static async Task<StoreTestApi> OpenAsync()
    {
        var token = await BenTestBase.SuperAdminTokenForHelpersAsync();
        Assert.That(token, Is.Not.Null, "Could not sign in to the API as the SuperAdmin.");
        var http = new HttpClient { BaseAddress = new Uri(BenTestBase.ApiUrlForHelpers), Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new StoreTestApi(http);
    }

    /// <summary>
    /// Sends without asserting, for cleanup whose refusals are expected — NUnit records a failed
    /// assertion even when it is caught, so SendAsync inside a try still fails the test.
    /// </summary>
    public async Task<System.Net.HttpStatusCode> TrySendAsync(HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, path);
        using var response = await http.SendAsync(request);
        return response.StatusCode;
    }

    public async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.IsSuccessStatusCode, Is.True, $"{method} {path} answered {(int)response.StatusCode}: {text}");
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    public async Task<Guid> CategoryAsync(string name)
        => (await SendAsync(HttpMethod.Post, "/api/admin/store/categories",
               new { name, slug = (string?)null, description = (string?)null, isActive = true, isNew = false }))
           .GetProperty("id").GetGuid();

    /// <summary>A product priced at <paramref name="price"/>, with a picture — optionally two sizes, optionally live.</summary>
    public async Task<JsonElement> ProductAsync(Guid categoryId, string name, bool twoSizes = false, bool live = false, decimal price = 39m)
    {
        var product = await SendAsync(HttpMethod.Post, "/api/admin/store/products", new { name, categoryId });
        var id = product.GetProperty("id").GetGuid();
        var variant = product.GetProperty("variants")[0];
        await SendAsync(HttpMethod.Put, $"/api/admin/store/products/{id}/variants/{variant.GetProperty("id").GetGuid()}",
            new { sku = variant.GetProperty("sku").GetString(), price, compareAtPrice = (decimal?)null, isActive = true, isDefault = true, sortOrder = 0, optionValueIds = Array.Empty<Guid>() });

        using (var form = new MultipartFormDataContent())
        {
            var bytes = new ByteArrayContent(await File.ReadAllBytesAsync(FixturePhoto));
            bytes.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(bytes, "file", "photo.jpg");
            using var upload = await http.PostAsync($"/api/admin/store/products/{id}/images", form);
            Assert.That(upload.IsSuccessStatusCode, Is.True, await upload.Content.ReadAsStringAsync());
        }

        if (twoSizes)
        {
            await SendAsync(HttpMethod.Put, $"/api/admin/store/products/{id}/options", new
            {
                options = new[]
                {
                    new { id = (Guid?)null, name = "Size", kind = 0, values = new[]
                    {
                        new { id = (Guid?)null, value = "Small", swatchHex = (string?)null, isActive = true },
                        new { id = (Guid?)null, value = "Large", swatchHex = (string?)null, isActive = true },
                    } },
                },
            });
            await SendAsync(HttpMethod.Post, $"/api/admin/store/products/{id}/variants/generate");
        }
        if (live) await SendAsync(HttpMethod.Post, $"/api/admin/store/products/{id}/activate");
        return await SendAsync(HttpMethod.Get, $"/api/admin/store/products/{id}");
    }

    public static string FixturePhoto => Path.Combine(AppContext.BaseDirectory, "Fixtures", "room-photo-1.jpg");

    /// <summary>Sets every variant of a product to <paramref name="onHand"/> — a stocktake, logged as a correction.</summary>
    public async Task SetStockAsync(JsonElement product, int onHand)
    {
        var id = product.GetProperty("id").GetGuid();
        foreach (var variant in product.GetProperty("variants").EnumerateArray())
            await SendAsync(HttpMethod.Post, $"/api/admin/store/products/{id}/variants/{variant.GetProperty("id").GetGuid()}/stock",
                new { delta = (int?)null, setTo = onHand, reason = 1, note = "e2e" });
    }

    /// <summary>
    /// Changes some store settings for one test and returns a way to put them back. The settings
    /// page saves them all at once, so this reads them, changes what was asked and saves the lot.
    /// </summary>
    public async Task<Func<Task>> WithSettingsAsync(
        bool? checkoutEnabled = null, decimal? flatRate = null, decimal? freeOver = null)
    {
        var before = await SendAsync(HttpMethod.Get, "/api/admin/store/settings");
        object Body(JsonElement s, bool? checkout, decimal? rate, decimal? over) => new
        {
            checkoutEnabled = checkout ?? s.GetProperty("checkoutEnabled").GetBoolean(),
            shippingFlatRate = rate ?? Nullable(s, "shippingFlatRate"),
            freeShippingThreshold = over ?? Nullable(s, "freeShippingThreshold"),
            lowStockThreshold = s.GetProperty("lowStockThreshold").ValueKind == JsonValueKind.Null ? (int?)null : s.GetProperty("lowStockThreshold").GetInt32(),
            shipFromStreet = Text(s, "shipFromStreet"), shipFromCity = Text(s, "shipFromCity"),
            shipFromState = Text(s, "shipFromState"), shipFromZip = Text(s, "shipFromZip"),
            supportEmail = Text(s, "supportEmail"),
            returnsWindowDays = s.GetProperty("returnsWindowDays").ValueKind == JsonValueKind.Null ? (int?)null : s.GetProperty("returnsWindowDays").GetInt32(),
            reservationMinutes = s.GetProperty("reservationMinutes").ValueKind == JsonValueKind.Null ? (int?)null : s.GetProperty("reservationMinutes").GetInt32(),
            linkEnabled = s.GetProperty("linkEnabled").GetBoolean(),
        };
        await SendAsync(HttpMethod.Put, "/api/admin/store/settings", Body(before, checkoutEnabled, flatRate, freeOver));
        return () => SendAsync(HttpMethod.Put, "/api/admin/store/settings", Body(before, null, null, null));
    }

    private static decimal? Nullable(JsonElement e, string name)
        => e.GetProperty(name).ValueKind == JsonValueKind.Null ? null : e.GetProperty(name).GetDecimal();

    private static string? Text(JsonElement e, string name)
        => e.GetProperty(name).ValueKind == JsonValueKind.Null ? null : e.GetProperty(name).GetString();

    /// <summary>The one shelf every buyable test product goes on.</summary>
    /// <remarks>
    /// It used to be a new shelf per product, which the e2e database keeps for ever: after a few
    /// weeks of runs the store had hundreds of shelves, and the listing's side list alone carried
    /// 75 KB into the page (found 09/24, StoreBrowseTests.The_carried_state_fits_the_connection).
    /// </remarks>
    public const string SharedShelf = "E2E test products";

    private async Task<Guid> SharedShelfAsync()
    {
        async Task<Guid?> FindAsync()
        {
            foreach (var c in (await SendAsync(HttpMethod.Get, "/api/admin/store/categories")).EnumerateArray())
                if (c.GetProperty("name").GetString() == SharedShelf) return c.GetProperty("id").GetGuid();
            return null;
        }
        if (await FindAsync() is { } found) return found;

        // Two fixtures can race to make it; the loser's "already a category called…" is fine.
        using var made = await http.PostAsync("/api/admin/store/categories", JsonContent.Create(
            new { name = SharedShelf, slug = (string?)null, description = (string?)null, isActive = true, isNew = false }));
        return await FindAsync() ?? throw new InvalidOperationException($"The shared test shelf could not be made: {(int)made.StatusCode}");
    }

    /// <summary>A live, one-variant product with stock — the card adds it straight to the cart.</summary>
    public async Task<JsonElement> BuyableAsync(string name, decimal price, int onHand = 10)
    {
        var product = await ProductAsync(await SharedShelfAsync(), name, live: true, price: price);
        await SetStockAsync(product, onHand);
        return product;
    }

    /// <summary>
    /// A paid guest order for one unit of <paramref name="product"/>, placed straight through the
    /// store's API in test checkout — cart, checkout, the dev pay route — as a buyer's browser would.
    /// </summary>
    /// <remarks>
    /// Anonymous calls from the test process share the checkout's ten-a-minute limit with the browser
    /// tests, so a 429 waits a minute and asks again.
    /// </remarks>
    public static async Task<Guid> PaidGuestOrderAsync(JsonElement product, string email)
    {
        using var guest = new HttpClient { BaseAddress = new Uri(BenTestBase.ApiUrlForHelpers), Timeout = TimeSpan.FromSeconds(60) };
        guest.DefaultRequestHeaders.Add("X-Ben-Cart",
            Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_'));

        async Task<HttpResponseMessage> PostAsync(string path, object body)
        {
            for (var attempt = 0; ; attempt++)
            {
                var response = await guest.PostAsync(path, JsonContent.Create(body));
                if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests || attempt == 2) return response;
                await Task.Delay(TimeSpan.FromSeconds(61));
            }
        }

        var variant = product.GetProperty("variants")[0].GetProperty("id").GetGuid();
        var added = await PostAsync("/api/store/cart/items", new { variantId = variant, quantity = 1 });
        Assert.That(added.IsSuccessStatusCode, Is.True, $"adding to the cart answered {(int)added.StatusCode}: {await added.Content.ReadAsStringAsync()}");

        var prepared = await PostAsync("/api/store/checkout/payment-intent", new
        {
            email,
            shipping = new { fullName = "Desk Buyer", phone = "615-555-0177", street1 = "3 Birch Rd", street2 = (string?)null, city = "Nashville", state = "TN", zip = "37203" },
            billing = (object?)null, billCompany = (string?)null, agreedToTerms = true, buyerNotes = (string?)null,
        });
        var text = await prepared.Content.ReadAsStringAsync();
        Assert.That(prepared.IsSuccessStatusCode, Is.True, $"the checkout answered {(int)prepared.StatusCode}: {text}");
        var orderId = JsonDocument.Parse(text).RootElement.GetProperty("orderId").GetGuid();

        var paid = await PostAsync($"/api/store/checkout/dev/simulate-payment/{orderId}", new { });
        Assert.That(paid.IsSuccessStatusCode, Is.True, $"the test payment answered {(int)paid.StatusCode}");
        return orderId;
    }

    public void Dispose() => http.Dispose();
}
