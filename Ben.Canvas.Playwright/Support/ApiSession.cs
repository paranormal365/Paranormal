using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ben.Canvas.Playwright.Support;

/// <summary>
/// The seeded account talking to the API directly, beside the browser: to find the case to open, to check what
/// the editor saved, and to play "somebody else" saving a newer copy. Boards it sees created are deleted at the end.
/// </summary>
/// <remarks>Relative paths on a base ending in "/", the same rule as the editor's own clients, so a /webapi mount is kept.</remarks>
public sealed class ApiSession : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    private ApiSession(HttpClient http) => _http = http;

    public List<Guid> CreatedBoards { get; } = [];

    public static async Task<ApiSession> SignInAsync(string apiUrl, string email, string password)
    {
        var http = new HttpClient { BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };
        using var login = await http.PostAsJsonAsync("login", new { email, password });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("accessToken").GetString();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new ApiSession(http);
    }

    public async Task<bool> CanvasIsOnAsync()
    {
        using var response = await _http.GetAsync("api/canvas-documents");
        return response.IsSuccessStatusCode;
    }

    /// <summary>The first organisation the account belongs to and a case in it.</summary>
    public async Task<(Guid OrganizationId, Guid CaseId)> CaseAsync()
    {
        var orgs = await _http.GetFromJsonAsync<JsonElement>("api/organizations", Web);
        foreach (var org in Items(orgs))
        {
            var orgId = org.GetProperty("id").GetGuid();
            using var response = await _http.GetAsync($"api/organizations/{orgId}/cases");
            if (!response.IsSuccessStatusCode) continue;
            var cases = await response.Content.ReadFromJsonAsync<JsonElement>(Web);
            var first = Items(cases).FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object) return (orgId, first.GetProperty("id").GetGuid());
        }

        throw new InvalidOperationException("The seeded account has no case to put a board on.");
    }

    public async Task<List<JsonElement>> BoardsAsync(Guid caseId) =>
        Items(await _http.GetFromJsonAsync<JsonElement>($"api/canvas-documents?caseId={caseId}", Web)).ToList();

    public Task<JsonElement> BoardAsync(Guid id) => _http.GetFromJsonAsync<JsonElement>($"api/canvas-documents/{id}", Web);

    /// <summary>Saves the server's own copy again, as another person would, moving the revision on.</summary>
    public async Task<int> SaveAsSomebodyElseAsync(Guid id)
    {
        var board = await BoardAsync(id);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/canvas-documents/{id}")
        {
            Content = new StringContent(board.GetProperty("documentJson").GetString()!, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"" + board.GetProperty("revision").GetInt32() + "\"");
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("revision").GetInt32();
    }

    public async Task DeleteBoardAsync(Guid id)
    {
        using var _ = await _http.DeleteAsync($"api/canvas-documents/{id}");
    }

    private static IEnumerable<JsonElement> Items(JsonElement json) =>
        json.ValueKind == JsonValueKind.Array ? json.EnumerateArray()
        : json.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array ? items.EnumerateArray()
        : [];

    public async ValueTask DisposeAsync()
    {
        foreach (var id in CreatedBoards.Distinct()) await DeleteBoardAsync(id);
        _http.Dispose();
    }
}
