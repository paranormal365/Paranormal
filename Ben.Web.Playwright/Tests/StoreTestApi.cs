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

    public void Dispose() => http.Dispose();
}
