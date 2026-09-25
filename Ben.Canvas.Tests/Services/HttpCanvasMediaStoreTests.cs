using Ben.Canvas.Core.Options;
using Ben.Canvas.Core.Text;
using Ben.Canvas.Editor.Services;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Services;

/// <summary>
/// Case pictures and files through the browser: uploads go to the case's files route, displayed pictures become
/// cached blob: addresses fetched with the token, the token never leaves for an address outside the API, a 401
/// is refreshed once, and every object URL is revoked on dispose.
/// </summary>
public sealed class HttpCanvasMediaStoreTests
{
    private const string Api = "https://ishaunted.test/webapi";
    private static readonly Guid Org = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Case = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid File1 = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private sealed class Tokens : ICanvasAccessTokenSource
    {
        public int Refreshes { get; private set; }
        public Task<string?> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken ct = default)
        {
            if (forceRefresh) Refreshes++;
            return Task.FromResult<string?>(Refreshes == 0 ? "old-token" : "new-token");
        }
    }

    private sealed class SignIn(bool signedIn) : ICanvasSignInState
    {
        public bool IsSignedIn => signedIn;
        public string? DisplayName => null;
    }

    private static (HttpCanvasMediaStore Store, FakeModule Blob, Tokens Tokens) Rig(bool signedIn = true, string? api = Api)
    {
        var js = new FakeModuleJs();
        var blob = js.Module("js/blobInterop.js");
        var n = 0;
        blob.On("fetchAsObjectUrl", a => new { status = 200, url = $"blob:fake/{++n}" })
            .On("uploadFromUrl", _ => new { status = 201, body = $$"""{"uploadFileId":"{{File1}}","fileName":"porch.jpg","contentType":"image/jpeg","fileSize":1234}""" })
            .On("revokeObjectUrl", _ => null);
        var tokens = new Tokens();
        var store = new HttpCanvasMediaStore(js, Microsoft.Extensions.Options.Options.Create(new CanvasEditorOptions { ApiBaseUrl = api }), tokens, new SignIn(signedIn));
        return (store, blob, tokens);
    }

    [Fact]
    public async Task An_upload_posts_the_device_file_to_the_cases_files_with_the_token()
    {
        var (store, blob, _) = Rig();

        var (upload, problem) = await store.UploadAsync(Org, Case, "blob:local/1", "porch.jpg", "From board \"Porch\"");

        Assert.Null(problem);
        Assert.Equal(File1, upload!.UploadFileId);
        var call = blob.Calls.Single(c => c.Name == "uploadFromUrl");
        Assert.Equal($"{Api}/api/orgs/{Org}/cases/{Case}/files", call.Args[0]);
        Assert.Equal("old-token", call.Args[1]);
        Assert.Equal("blob:local/1", call.Args[2]);
        Assert.Equal("porch.jpg", call.Args[3]);
    }

    [Fact]
    public async Task A_400_sentence_from_the_server_is_passed_through()
    {
        var (store, blob, _) = Rig();
        blob.On("uploadFromUrl", _ => new { status = 400, body = "This case has used its storage allowance." });

        var (upload, problem) = await store.UploadAsync(Org, Case, "blob:local/1", "porch.jpg", null);

        Assert.Null(upload);
        Assert.Equal("This case has used its storage allowance.", problem);
    }

    [Fact]
    public async Task A_403_upload_says_the_account_may_not_add_files()
    {
        var (store, blob, _) = Rig();
        blob.On("uploadFromUrl", _ => new { status = 403, body = "" });
        Assert.Equal(CanvasCopy.Sentences.AddFilesForbidden, (await store.UploadAsync(Org, Case, "blob:local/1", "a.jpg", null)).Problem);
    }

    [Fact]
    public async Task A_displayed_picture_is_fetched_once_and_cached()
    {
        var (store, blob, _) = Rig();

        var first = await store.GetDisplayUrlAsync(File1, thumbnail: true);
        var second = await store.GetDisplayUrlAsync(File1, thumbnail: true);

        Assert.Equal("blob:fake/1", first);
        Assert.Equal(first, second);
        var call = Assert.Single(blob.Calls, c => c.Name == "fetchAsObjectUrl");
        Assert.Equal($"{Api}/api/upload-files/{File1}/thumbnail", call.Args[0]);
    }

    [Fact]
    public async Task Thumbnail_and_download_are_cached_separately()
    {
        var (store, blob, _) = Rig();
        await store.GetDisplayUrlAsync(File1, thumbnail: true);
        await store.GetDisplayUrlAsync(File1, thumbnail: false);

        Assert.Equal(2, blob.CountOf("fetchAsObjectUrl"));
        Assert.Equal($"{Api}/api/upload-files/{File1}/download", blob.Calls.Last(c => c.Name == "fetchAsObjectUrl").Args[0]);
    }

    [Fact]
    public async Task A_401_refreshes_the_token_once_and_tries_again()
    {
        var (store, blob, tokens) = Rig();
        var calls = 0;
        blob.On("fetchAsObjectUrl", a => ++calls == 1 ? new { status = 401, url = (string?)null } : new { status = 200, url = (string?)$"blob:fake/{a[1]}" });

        var url = await store.GetDisplayUrlAsync(File1, thumbnail: false);

        Assert.Equal("blob:fake/new-token", url);
        Assert.Equal(1, tokens.Refreshes);
    }

    [Fact]
    public async Task The_token_never_goes_to_an_address_outside_the_api()
    {
        var (store, blob, _) = Rig();

        Assert.Null(await store.GetDisplayUrlAsync("https://example.com/og.png"));
        Assert.Null(await store.GetDisplayUrlAsync("https://ishaunted.test/webapiX/api/link-unfurl/image?url=x"));
        Assert.Equal(0, blob.CountOf("fetchAsObjectUrl"));

        Assert.NotNull(await store.GetDisplayUrlAsync($"{Api}/api/link-unfurl/image?url=https%3A%2F%2Fexample.com%2Fog.png"));
    }

    [Fact]
    public async Task Signed_out_nothing_is_fetched_or_uploaded()
    {
        var (store, blob, _) = Rig(signedIn: false);

        Assert.False(store.IsAvailable);
        Assert.Null(await store.GetDisplayUrlAsync(File1, thumbnail: true));
        Assert.Null((await store.UploadAsync(Org, Case, "blob:local/1", "a.jpg", null)).Upload);
        Assert.Empty(blob.Calls);
    }

    [Fact]
    public async Task A_failed_fetch_answers_null_and_is_not_cached()
    {
        var (store, blob, _) = Rig();
        blob.On("fetchAsObjectUrl", _ => new { status = 404, url = (string?)null });

        Assert.Null(await store.GetDisplayUrlAsync(File1, thumbnail: true));
        Assert.Null(await store.GetDisplayUrlAsync(File1, thumbnail: true));
        Assert.Equal(2, blob.CountOf("fetchAsObjectUrl"));
    }

    [Fact]
    public async Task Dispose_revokes_every_object_url()
    {
        var (store, blob, _) = Rig();
        await store.GetDisplayUrlAsync(File1, thumbnail: true);
        await store.GetDisplayUrlAsync(File1, thumbnail: false);

        await store.DisposeAsync();

        Assert.Equal(["blob:fake/1", "blob:fake/2"], blob.Calls.Where(c => c.Name == "revokeObjectUrl").Select(c => (string)c.Args[0]!).Order());
    }
}
