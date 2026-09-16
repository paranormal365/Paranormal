using System.Globalization;
using System.Net;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Core.Text;
using Ben.Canvas.Editor.Services;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Services;

/// <summary>
/// Saving boards to the API: create then replace with If-Match, a 409 handed back with the server's copy, the
/// /webapi mount kept, nothing sent while signed out, and a sentence for every refusal.
/// </summary>
public sealed class HttpCanvasServerStoreTests
{
    private const string Api = "https://ishaunted.test/webapi";

    private static readonly Guid BoardId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CaseId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static string Record(int revision, bool? canEdit = null) =>
        $$"""
        {"id":"{{BoardId}}","caseId":"{{CaseId}}","organizationId":"33333333-3333-3333-3333-333333333333","name":"Porch",
         "documentJson":"{\"title\":\"Porch\"}","revision":{{revision}},"publishedAtUtc":null,"publishedUploadFileId":null,
         "dateCreated":"2026-09-15T10:00:00Z","dateUpdated":null,"createdByAppUserId":"44444444-4444-4444-4444-444444444444",
         "createdByName":"Sarah Mitchell"{{(canEdit is null ? "" : $",\"canEdit\":{canEdit.Value.ToString().ToLowerInvariant()}")}}}
        """;

    private sealed class SignIn(bool signedIn) : ICanvasSignInState
    {
        public bool IsSignedIn => signedIn;
        public string? DisplayName => signedIn ? "sarah.mitchell@benco.dev" : null;
    }

    private static HttpCanvasServerStore Store(StubHandler handler, string? api = Api, bool signedIn = true) =>
        new(new SingleClientFactory(handler),
            Microsoft.Extensions.Options.Options.Create(new CanvasEditorOptions { ApiBaseUrl = api, ServerSave = api is not null }),
            new SignIn(signedIn));

    [Fact]
    public async Task A_first_save_posts_to_the_case_and_a_second_puts_with_if_match()
    {
        var handler = new StubHandler(HttpStatusCode.Created, Record(1)).Then(HttpStatusCode.OK, Record(4));
        var store = Store(handler);

        var first = await store.SaveAsync("""{"title":"Porch"}""", null, 0, CaseId);
        Assert.Equal(CanvasSaveOutcome.Saved, first.Outcome);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal($"{Api}/api/canvas-documents?caseId={CaseId}", handler.Urls[0]);

        var second = await store.SaveAsync("""{"title":"Porch"}""", BoardId, 3, CaseId);
        Assert.Equal(CanvasSaveOutcome.Saved, second.Outcome);
        Assert.Equal(4, second.Document!.Revision);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        Assert.Equal($"{Api}/api/canvas-documents/{BoardId}", handler.Urls[1]);
        Assert.Equal("\"3\"", string.Join("", handler.Requests[1].Headers.GetValues("If-Match")));
    }

    [Fact]
    public async Task A_409_comes_back_as_a_conflict_carrying_the_server_copy()
    {
        var store = Store(new StubHandler(HttpStatusCode.Conflict, Record(7)));

        var result = await store.SaveAsync("{}", BoardId, 3, CaseId);

        Assert.Equal(CanvasSaveOutcome.Conflict, result.Outcome);
        Assert.Equal(7, result.Document!.Revision);
        Assert.Equal("""{"title":"Porch"}""", result.Document.DocumentJson);
        Assert.Equal(CanvasCopy.Sentences.NewerServerCopy(7), result.Problem);
    }

    [Fact]
    public async Task The_webapi_mount_is_kept_on_every_route()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Record(1));
        var store = Store(handler, api: Api + "/");

        await store.GetAsync(BoardId);
        await store.PublishAsync(BoardId, [1, 2, 3], "Porch");
        handler.Then(HttpStatusCode.NoContent, "");
        await store.DeleteAsync(BoardId);

        Assert.All(handler.Urls, u => Assert.StartsWith(Api + "/api/canvas-documents/", u, StringComparison.Ordinal));
        Assert.Equal($"{Api}/api/canvas-documents/{BoardId}/publish", handler.Urls[1]);
    }

    [Fact]
    public async Task Nothing_is_sent_while_signed_out()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Record(1));
        var store = Store(handler, signedIn: false);

        Assert.False(store.IsAvailable);
        var result = await store.SaveAsync("{}", null, 0, CaseId);

        Assert.Equal(CanvasSaveOutcome.Failed, result.Outcome);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Without_an_api_the_store_is_unavailable_and_says_so()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Record(1));
        var store = Store(handler, api: null);

        Assert.False(store.IsAvailable);
        var (document, problem) = await store.GetAsync(BoardId);

        Assert.Null(document);
        Assert.Equal(CanvasCopy.Sentences.ServerNotConfigured, problem);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, CanvasCopy.Sentences.SignInExpired, false)]
    [InlineData(HttpStatusCode.Forbidden, CanvasCopy.Sentences.SaveForbidden, true)]
    [InlineData(HttpStatusCode.NotFound, CanvasCopy.Sentences.BoardGoneFromServer, false)]
    public async Task A_refusal_is_a_sentence(HttpStatusCode status, string sentence, bool forbidden)
    {
        var result = await Store(new StubHandler(status, "")).SaveAsync("{}", BoardId, 1, CaseId);

        Assert.Equal(CanvasSaveOutcome.Failed, result.Outcome);
        Assert.Equal(sentence, result.Problem);
        Assert.Equal(forbidden, result.Forbidden);
    }

    [Fact]
    public async Task A_400_passes_the_servers_own_sentence_through()
    {
        var result = await Store(new StubHandler(HttpStatusCode.BadRequest, "This organisation's plan has lapsed, so its cases are read-only."))
            .SaveAsync("{}", BoardId, 1, CaseId);

        Assert.Equal("This organisation's plan has lapsed, so its cases are read-only.", result.Problem);
    }

    [Fact]
    public async Task An_unreachable_server_is_a_sentence_not_an_exception()
    {
        var store = new HttpCanvasServerStore(new SingleClientFactory(new ThrowingHandler()),
            Microsoft.Extensions.Options.Options.Create(new CanvasEditorOptions { ApiBaseUrl = Api, ServerSave = true }));

        var result = await store.SaveAsync("{}", BoardId, 1, CaseId);

        Assert.Equal(CanvasSaveOutcome.Failed, result.Outcome);
        Assert.Equal(CanvasCopy.Sentences.ServerUnreachable, result.Problem);
    }

    [Fact]
    public async Task Publish_sends_a_png_part_named_file()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Record(2));
        await Store(handler).PublishAsync(BoardId, [0x89, 0x50, 0x4E, 0x47], "Porch");

        var content = Assert.IsType<MultipartFormDataContent>(handler.Requests[0].Content);
        var part = Assert.Single(content);
        Assert.Equal("file", part.Headers.ContentDisposition!.Name!.Trim('"'));
        Assert.Equal("Porch.png", part.Headers.ContentDisposition.FileName!.Trim('"'));
        Assert.Equal("image/png", part.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task If_match_uses_invariant_digits()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("ar-SA") { NumberFormat = { NativeDigits = ["٠", "١", "٢", "٣", "٤", "٥", "٦", "٧", "٨", "٩"], DigitSubstitution = DigitShapes.NativeNational } };
        try
        {
            var handler = new StubHandler(HttpStatusCode.OK, Record(13));
            await Store(handler).SaveAsync("{}", BoardId, 12, CaseId);
            Assert.Equal("\"12\"", string.Join("", handler.Requests[0].Headers.GetValues("If-Match")));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task Can_edit_is_read_and_a_server_that_omits_it_means_editable()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Record(1, canEdit: false)).Then(HttpStatusCode.OK, Record(1));
        var store = Store(handler);

        Assert.False((await store.GetAsync(BoardId)).Document!.CanEdit);
        Assert.True((await store.GetAsync(BoardId)).Document!.CanEdit);
    }

    [Fact]
    public async Task A_case_list_comes_back_as_summaries()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "[" + Record(2) + "]");
        var (items, problem) = await Store(handler).ListAsync(CaseId);

        Assert.Null(problem);
        Assert.Equal("Porch", Assert.Single(items!).Name);
        Assert.Equal($"{Api}/api/canvas-documents?caseId={CaseId}", handler.LastUrl);
    }
}
