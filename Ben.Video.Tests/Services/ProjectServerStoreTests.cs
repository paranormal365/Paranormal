using System.Net;
using System.Text;
using Ben.Video.Editor.Extensions;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Saving a project to the server updates it rather than making another copy.
/// </summary>
/// <remarks>
/// Every save posted a new row, so a project saved five times became five projects with the same
/// name, and the server list filled with copies of one thing (2026-09-05 audit, persistence-13).
/// </remarks>
public sealed class HttpProjectServerStoreTests
{
    private const string BaseUrl = "https://api.test/api/video-projects";

    private static (HttpProjectServerStore Store, SpyHandler Handler) Create(
        HttpStatusCode status = HttpStatusCode.OK,
        string body = """{"id":"11111111-1111-1111-1111-111111111111"}""",
        string? postUrl = BaseUrl,
        bool? signedIn = null)
    {
        var handler = new SpyHandler(status, body);
        var options = Options.Create(new VideoEditorOptions { DocumentPostUrl = postUrl });

        return (new HttpProjectServerStore(
            new SingleClientFactory(handler),
            options,
            signedIn is null ? null : new StubSignInState(signedIn.Value)), handler);
    }

    [Fact]
    public async Task A_project_that_is_not_on_the_server_yet_is_created()
    {
        var (store, handler) = Create();

        await store.SaveAsync(new ProjectFile(), existingId: null);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(BaseUrl, handler.LastUrl);
    }

    /// <summary>
    /// The whole point: a second save of the same project updates it.
    /// </summary>
    [Fact]
    public async Task A_project_already_on_the_server_is_updated_not_duplicated()
    {
        var (store, handler) = Create();
        var existing = Guid.Parse("22222222-2222-2222-2222-222222222222");

        await store.SaveAsync(new ProjectFile(), existing);

        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.EndsWith(existing.ToString(), handler.LastUrl);
    }

    [Fact]
    public async Task A_case_project_is_created_against_its_case()
    {
        var (store, handler) = Create();
        var caseId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        await store.SaveAsync(new ProjectFile(), existingId: null, caseId: caseId);

        Assert.Contains($"caseId={caseId}", handler.LastUrl);
    }

    [Fact]
    public async Task The_servers_id_comes_back_so_the_next_save_updates()
    {
        var (store, _) = Create();

        var (id, problem) = await store.SaveAsync(new ProjectFile(), existingId: null);

        Assert.Null(problem);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), id);
    }

    /// <summary>
    /// A save that did not happen has to say so. The editor reports it, and the project stays
    /// unsaved rather than being marked done.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "sign-in has expired")]
    [InlineData(HttpStatusCode.Forbidden,    "not allowed")]
    [InlineData(HttpStatusCode.NotFound,     "no longer on the server")]
    public async Task A_refused_save_explains_itself(HttpStatusCode status, string expected)
    {
        var (store, _) = Create(status, "");

        var (id, problem) = await store.SaveAsync(new ProjectFile(), existingId: null);

        Assert.Null(id);
        Assert.Contains(expected, problem, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A host with nowhere to save to says so rather than offering the option.
    /// </summary>
    [Fact]
    public void An_editor_with_no_server_configured_is_not_available()
    {
        var (store, _) = Create(postUrl: null);

        Assert.False(store.IsAvailable);
    }

    [Fact]
    public void An_editor_with_a_server_configured_is()
    {
        var (store, _) = Create();

        Assert.True(store.IsAvailable);
    }

    /// <summary>
    /// The other half of the same question: a server exists, but nobody is signed in to it.
    /// </summary>
    /// <remarks>
    /// The standalone editor showed Save to Server while signed out, and the button could only
    /// ever answer 401 (2026-09-05 audit, F13's other half).
    /// </remarks>
    [Fact]
    public void A_signed_out_person_is_not_offered_a_server_to_save_to()
    {
        var (store, _) = Create(signedIn: false);

        Assert.False(store.IsAvailable);
    }

    [Fact]
    public void A_signed_in_person_is()
    {
        var (store, _) = Create(signedIn: true);

        Assert.True(store.IsAvailable);
    }

    /// <summary>
    /// A host that cannot answer the question keeps the old behaviour, which is the right one for
    /// a host with no accounts at all.
    /// </summary>
    [Fact]
    public void A_host_that_knows_nothing_about_sign_in_still_offers_the_server()
    {
        var (store, _) = Create();

        Assert.True(store.IsAvailable);
    }

    // ── Support ───────────────────────────────────────────────────────────────

    private sealed class StubSignInState(bool signedIn) : IEditorSignInState
    {
        public bool IsSignedIn => signedIn;
    }

    private sealed class SpyHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpMethod? LastMethod { get; private set; }
        public string LastUrl { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            LastMethod = request.Method;
            LastUrl    = request.RequestUri!.ToString();

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }
}
