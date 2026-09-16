using Ben.Canvas.Core.Model;
using Ben.Canvas.Editor.Components.Nodes;
using Ben.Canvas.Editor.Services;
using Ben.Canvas.Tests.Components;
using Ben.Canvas.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ben.Canvas.Tests.Services;

/// <summary>
/// Link cards fill in after they appear (R34): a pasted address gets its preview without an undo step, a card
/// that only got its host is asked again a day later when signed in, a view-only board is left alone, and the
/// card's picture and a case picture are fetched through the media store rather than a bare img src.
/// </summary>
public sealed class LinkPreviewResolverTests
{
    private sealed class FakePreviews : ILinkPreviewProvider
    {
        public List<string> Asked { get; } = [];
        public LinkPreviewTier Tier { get; set; } = LinkPreviewTier.Unfurled;

        public Task<LinkPreviewCard> GetAsync(string url, CancellationToken ct = default)
        {
            Asked.Add(url);
            return Task.FromResult(Tier == LinkPreviewTier.HostOnly
                ? new LinkPreviewCard(url, "example.com", "Link", null, null, null, null, LinkPreviewTier.HostOnly)
                : new LinkPreviewCard(url, "example.com", "Link", "Example Domain", "For examples.", "https://example.com/og.png", "Example", Tier));
        }
    }

    private sealed class SignIn(bool signedIn) : ICanvasSignInState
    {
        public bool IsSignedIn => signedIn;
        public string? DisplayName => null;
    }

    private static CanvasNode LinkBlock(string url, LinkPreviewTier tier = LinkPreviewTier.None, DateTime? fetched = null) =>
        new() { Type = CanvasNodeType.Link, Width = 320, Height = 140, Data = new LinkData { Url = url, Tier = tier, FetchedAtUtc = fetched } };

    private static (LinkPreviewResolver Resolver, Core.Commands.CanvasStore Store, FakePreviews Previews, BoardAccess Access) Rig(bool signedIn = true)
    {
        var store = TestBoards.Store();
        var previews = new FakePreviews();
        var access = new BoardAccess();
        var resolver = new LinkPreviewResolver(store, previews, access, new SignIn(signedIn)) { UtcNow = () => new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc) };
        return (resolver, store, previews, access);
    }

    [Fact]
    public async Task A_pasted_address_gets_its_preview_without_an_undo_step()
    {
        var (resolver, store, _, _) = Rig();
        var node = LinkBlock("https://example.com/a");
        store.AddNode(node);

        Assert.Equal(1, await resolver.ResolveAsync());

        var link = (LinkData)store.FindNode(node.Id)!.Data;
        Assert.Equal("Example Domain", link.Title);
        Assert.Equal("https://example.com/og.png", link.ImageSourceUrl);
        Assert.Equal(LinkPreviewTier.Unfurled, link.Tier);
        Assert.Equal("Add link", store.UndoDescription);
        Assert.True(store.Undo());
        Assert.Empty(store.Document.Nodes);
    }

    [Fact]
    public async Task A_card_that_only_got_its_host_keeps_the_address_and_is_not_asked_again_at_once()
    {
        var (resolver, store, previews, _) = Rig();
        previews.Tier = LinkPreviewTier.HostOnly;
        store.AddNode(LinkBlock("https://example.com/a/b"));

        await resolver.ResolveAsync();
        await resolver.ResolveAsync(includeStale: true);

        var link = (LinkData)store.Document.Nodes[0].Data;
        Assert.Equal(LinkPreviewTier.HostOnly, link.Tier);
        Assert.Equal("https://example.com/a/b", link.Url);
        Assert.Single(previews.Asked);
    }

    [Fact]
    public async Task A_day_old_host_only_card_is_asked_again_when_signed_in()
    {
        var (resolver, store, previews, _) = Rig();
        store.AddNode(LinkBlock("https://example.com/a", LinkPreviewTier.HostOnly, new DateTime(2026, 9, 14, 11, 0, 0, DateTimeKind.Utc)));

        await resolver.ResolveAsync(includeStale: false);
        Assert.Empty(previews.Asked);

        await resolver.ResolveAsync(includeStale: true);
        Assert.Single(previews.Asked);
        Assert.Equal(LinkPreviewTier.Unfurled, ((LinkData)store.Document.Nodes[0].Data).Tier);
    }

    [Fact]
    public async Task Signed_out_a_host_only_card_is_not_asked_again()
    {
        var (resolver, store, previews, _) = Rig(signedIn: false);
        store.AddNode(LinkBlock("https://example.com/a", LinkPreviewTier.HostOnly, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));

        await resolver.ResolveAsync(includeStale: true);

        Assert.Empty(previews.Asked);
    }

    [Fact]
    public async Task A_view_only_board_is_left_as_it_is()
    {
        var (resolver, store, previews, access) = Rig();
        store.AddNode(LinkBlock("https://example.com/a"));
        access.Set(false);

        Assert.Equal(0, await resolver.ResolveAsync());
        Assert.Empty(previews.Asked);
    }

    [Fact]
    public async Task An_address_changed_while_asking_keeps_the_new_address()
    {
        var store = TestBoards.Store();
        var node = LinkBlock("https://example.com/old");
        store.AddNode(node);
        var previews = new ChangingPreviews(store, node.Id);
        var resolver = new LinkPreviewResolver(store, previews, new BoardAccess());

        await resolver.ResolveAsync();

        var link = (LinkData)store.FindNode(node.Id)!.Data;
        Assert.Equal("https://example.com/new", link.Url);
        Assert.Null(link.Title);
    }

    /// <summary>Changes the block's address while the preview is on its way.</summary>
    private sealed class ChangingPreviews(Core.Commands.CanvasStore store, Guid nodeId) : ILinkPreviewProvider
    {
        public Task<LinkPreviewCard> GetAsync(string url, CancellationToken ct = default)
        {
            store.SetNodeDataWithoutHistory(nodeId, d => ((LinkData)d).Url = "https://example.com/new");
            return Task.FromResult(new LinkPreviewCard(url, "example.com", "Link", "Old page", null, null, null, LinkPreviewTier.Unfurled));
        }
    }

    // ── what the blocks show ───────────────────────────────────────────────────

    private sealed class FakeMedia : ICanvasMediaStore
    {
        public List<string> Fetched { get; } = [];
        public bool IsAvailable => true;
        public Task<(CanvasMediaUpload? Upload, string? Problem)> UploadAsync(Guid organizationId, Guid caseId, string sourceUrl, string fileName, string? description, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<string?> GetDisplayUrlAsync(Guid uploadFileId, bool thumbnail, CancellationToken ct = default)
        {
            Fetched.Add($"{uploadFileId}:{thumbnail}");
            return Task.FromResult<string?>("blob:case/" + uploadFileId);
        }

        // Not reached by these tests: neither exercises the picker.
    public Task<(IReadOnlyList<CanvasCaseFile> Files, string? Problem)> ListCaseFilesAsync(
        Guid organizationId, Guid caseId, CancellationToken ct = default)
        => Task.FromResult<(IReadOnlyList<CanvasCaseFile>, string?)>(([], null));

    public Task<string?> GetDisplayUrlAsync(string apiUrl, CancellationToken ct = default)
        {
            Fetched.Add(apiUrl);
            return Task.FromResult<string?>("blob:proxy/1");
        }
    }

    private static Action<IServiceCollection> WithMedia(FakeMedia media) => services =>
    {
        services.RemoveAll<ICanvasMediaStore>();
        services.AddScoped<ICanvasMediaStore>(_ => media);
        services.Configure<Core.Options.CanvasEditorOptions>(o => o.ApiBaseUrl = "https://ishaunted.test/webapi");
    };

    [Fact]
    public async Task A_case_picture_not_on_this_device_is_fetched_from_the_case()
    {
        var media = new FakeMedia();
        var upload = Guid.NewGuid();
        var node = new CanvasNode { Type = CanvasNodeType.Image, Width = 320, Height = 240, Data = new ImageData { UploadFileId = upload } };

        var html = await RenderHelper.RenderAsync<ImageNode>(new Dictionary<string, object?> { ["Node"] = node, ["Data"] = node.Data }, WithMedia(media));

        Assert.Contains($"src=\"blob:case/{upload}\"", html);
        Assert.Equal([$"{upload}:True"], media.Fetched);
        Assert.DoesNotContain("/api/upload-files", html);
    }

    [Fact]
    public async Task A_link_cards_picture_comes_through_the_proxy_with_the_token_not_a_bare_img_src()
    {
        var media = new FakeMedia();
        var node = LinkBlock("https://example.com/a", LinkPreviewTier.Unfurled);
        ((LinkData)node.Data).ImageSourceUrl = "https://example.com/og.png";

        var html = await RenderHelper.RenderAsync<LinkNode>(new Dictionary<string, object?> { ["Node"] = node, ["Data"] = node.Data }, WithMedia(media));

        Assert.Contains("src=\"blob:proxy/1\"", html);
        Assert.Equal(["https://ishaunted.test/webapi/api/link-unfurl/image?url=https%3A%2F%2Fexample.com%2Fog.png"], media.Fetched);
        Assert.DoesNotContain("link-unfurl/image", html);
    }
}
