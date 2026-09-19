using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Persistence;
using Ben.Canvas.Core.Serialization;
using Ben.Canvas.Core.Text;
using Ben.Canvas.Editor.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ben.Canvas.Tests.Services;

/// <summary>
/// Save to case and opening a case's board: create then replace with the returned revision, a newer copy kept as
/// a choice rather than overwritten, the device copy reused, pictures uploaded before the board, and a refusal
/// that turns the board view-only.
/// </summary>
public sealed class CanvasServerSessionTests
{
    private static readonly Guid Case = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Org = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>A case's boards in memory, answering like the API.</summary>
    private sealed class FakeServer : ICanvasServerStore
    {
        public Dictionary<Guid, CanvasServerDocument> Boards { get; } = [];
        public List<(Guid? ExistingId, int Revision, string Json)> Saves { get; } = [];
        public bool Available { get; set; } = true;
        public bool CanEdit { get; set; } = true;
        public Func<string, string>? Rewrite { get; set; }
        public CanvasSaveResult? NextRefusal { get; set; }
        public List<string> Log { get; } = [];

        public bool IsAvailable => Available;

        public CanvasServerDocument Put(string json, Guid? id = null, int revision = 1, DateTime? updated = null)
        {
            var board = new CanvasServerDocument(id ?? Guid.NewGuid(), Case, Org, "Porch", json, revision, null, null, Guid.Empty, null,
                new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc), updated, CanEdit);
            Boards[board.Id] = board;
            return board;
        }

        public Task<(IReadOnlyList<CanvasServerSummary>? Items, string? Problem)> ListAsync(Guid? caseId, CancellationToken ct = default) =>
            Task.FromResult<(IReadOnlyList<CanvasServerSummary>?, string?)>((Boards.Values
                .Select(b => new CanvasServerSummary(b.Id, b.CaseId, b.OrganizationId, b.Name, b.Revision, null, Guid.Empty, null, b.DateCreated, b.DateUpdated, b.CanEdit))
                .ToList(), null));

        public Task<(CanvasServerDocument? Document, string? Problem)> GetAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Boards.TryGetValue(id, out var b) ? ((CanvasServerDocument?)(b with { CanEdit = CanEdit }), (string?)null) : (null, CanvasCopy.Sentences.BoardGoneFromServer));

        /// <summary>
        /// A board link follows the PUBLISHED copy, so this answers only for a board that has one —
        /// which is what makes "the target has gone" a state the tests can reach.
        /// </summary>
        public Task<(CanvasServerDocument? Document, string? Problem)> GetPublishedAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Boards.TryGetValue(id, out var b) && b.PublishedAtUtc is not null
                ? ((CanvasServerDocument?)(b with { CanEdit = CanEdit }), (string?)null)
                : (null, CanvasCopy.Sentences.BoardGoneFromServer));

        public Task<CanvasSaveResult> SaveAsync(string documentJson, Guid? existingId, int revision, Guid? caseId, CancellationToken ct = default)
        {
            Log.Add("save");
            Saves.Add((existingId, revision, documentJson));
            if (NextRefusal is { } refusal) return Task.FromResult(refusal);

            var json = Rewrite?.Invoke(documentJson) ?? documentJson;
            if (existingId is null) return Task.FromResult(new CanvasSaveResult(CanvasSaveOutcome.Saved, Put(json), null));

            var current = Boards[existingId.Value];
            if (current.Revision != revision) return Task.FromResult(new CanvasSaveResult(CanvasSaveOutcome.Conflict, current, CanvasCopy.Sentences.NewerServerCopy(current.Revision)));
            return Task.FromResult(new CanvasSaveResult(CanvasSaveOutcome.Saved, Put(json, current.Id, current.Revision + 1), null));
        }

        public Guid? Published { get; private set; }

        public Task<(CanvasServerDocument? Document, string? Problem)> PublishAsync(Guid id, byte[] png, string fileName, CancellationToken ct = default)
        {
            Log.Add("publish");
            Published = id;
            var board = Boards[id] with { PublishedAtUtc = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc) };
            Boards[id] = board;
            return Task.FromResult<(CanvasServerDocument?, string?)>((board, null));
        }

        public Task<(bool Ok, string? Problem)> DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeMedia(FakeServer server) : ICanvasMediaStore
    {
        public List<(string Source, string Name)> Uploads { get; } = [];
        public string? Refuse { get; set; }
        public bool IsAvailable => true;

        public Task<(CanvasMediaUpload? Upload, string? Problem)> UploadAsync(Guid organizationId, Guid caseId, string sourceUrl, string fileName, string? description, CancellationToken ct = default)
        {
            server.Log.Add("upload");
            if (Refuse is not null) return Task.FromResult<(CanvasMediaUpload?, string?)>((null, Refuse));
            Uploads.Add((sourceUrl, fileName));
            return Task.FromResult<(CanvasMediaUpload?, string?)>((new CanvasMediaUpload(Guid.NewGuid(), "image/png", 10), null));
        }

        public Task<string?> GetDisplayUrlAsync(Guid uploadFileId, bool thumbnail, CancellationToken ct = default) => Task.FromResult<string?>(null);
        // Not reached by these tests: neither exercises the picker.
    public Task<(IReadOnlyList<CanvasCaseFile> Files, string? Problem)> ListCaseFilesAsync(
        Guid organizationId, Guid caseId, CancellationToken ct = default)
        => Task.FromResult<(IReadOnlyList<CanvasCaseFile>, string?)>(([], null));

        public Task<(IReadOnlyList<CanvasClientNote> Notes, string? Problem)> ListClientNotesAsync(
            Guid organizationId, Guid caseId, CancellationToken ct = default) => Task.FromResult<(IReadOnlyList<CanvasClientNote>, string?)>(([], null));

    public Task<string?> GetDisplayUrlAsync(string apiUrl, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    private sealed class Rig
    {
        public DeviceRig Device { get; } = new();
        public FakeServer Server { get; } = new();
        public FakeMedia Media { get; }
        public BoardAccess Access { get; } = new();
        public CanvasServerSession Session { get; }

        public Rig()
        {
            Media = new FakeMedia(Server);
            Session = new CanvasServerSession(Device.Store, Device.Documents, Device.Assets, Server, Media, Access, NullLogger<CanvasServerSession>.Instance);
        }

        public async Task<Rig> StartedAsync()
        {
            await Device.StartedAsync();
            Device.Documents.New(Case);
            Device.Documents.SetOrganization(Org);
            return this;
        }
    }

    private static CanvasNode Card(string title = "Cold spot") =>
        new() { Type = CanvasNodeType.Card, Width = 280, Height = 200, Data = new CardData { Title = title } };

    [Fact]
    public async Task A_first_save_creates_the_board_and_a_second_replaces_it_at_the_returned_revision()
    {
        var rig = await new Rig().StartedAsync();
        rig.Device.Store.AddNode(Card());

        Assert.Null(await rig.Session.SaveToCaseAsync());
        var id = rig.Device.Documents.CurrentServerId!.Value;
        Assert.Equal(1, rig.Device.Store.Document.Revision);
        Assert.Equal(SaveStateKind.SavedServer, rig.Device.Documents.State.Kind);

        rig.Device.Store.AddNode(Card("Footsteps"));
        Assert.Null(await rig.Session.SaveToCaseAsync());

        Assert.Equal([(null, 0), (id, 1)], rig.Server.Saves.Select(s => (s.ExistingId, s.Revision)));
        Assert.Equal(2, rig.Device.Store.Document.Revision);
        Assert.False(rig.Device.Documents.ChangedSinceServer);
    }

    [Fact]
    public async Task A_save_to_case_is_not_an_undo_step_and_keeps_undo()
    {
        var rig = await new Rig().StartedAsync();
        rig.Device.Store.AddNode(Card());

        await rig.Session.SaveToCaseAsync();

        Assert.True(rig.Device.Store.CanUndo);
        Assert.Equal("Add card", rig.Device.Store.UndoDescription);
    }

    [Fact]
    public async Task A_newer_copy_is_kept_as_a_choice_not_overwritten()
    {
        var rig = await new Rig().StartedAsync();
        rig.Device.Store.AddNode(Card());
        await rig.Session.SaveToCaseAsync();
        var id = rig.Device.Documents.CurrentServerId!.Value;
        var theirs = rig.Server.Put(rig.Server.Boards[id].DocumentJson, id, revision: 5);

        rig.Device.Store.AddNode(Card("Mine"));
        var problem = await rig.Session.SaveToCaseAsync();

        Assert.Equal(CanvasCopy.Sentences.NewerServerCopy(5), problem);
        Assert.Equal(SaveStateKind.ServerConflict, rig.Device.Documents.State.Kind);
        Assert.Equal(theirs, rig.Session.PendingConflict);
        Assert.Equal(5, rig.Server.Boards[id].Revision);
    }

    [Fact]
    public async Task Keep_mine_saves_over_the_newer_copy()
    {
        var rig = await new Rig().StartedAsync();
        rig.Device.Store.AddNode(Card());
        await rig.Session.SaveToCaseAsync();
        var id = rig.Device.Documents.CurrentServerId!.Value;
        rig.Server.Put(rig.Server.Boards[id].DocumentJson, id, revision: 5);
        rig.Device.Store.AddNode(Card("Mine"));
        await rig.Session.SaveToCaseAsync();

        Assert.Null(await rig.Session.KeepMineAsync());

        Assert.Equal(6, rig.Server.Boards[id].Revision);
        Assert.Contains("Mine", rig.Server.Boards[id].DocumentJson);
        Assert.Null(rig.Session.PendingConflict);
    }

    [Fact]
    public async Task Take_theirs_opens_the_newer_copy_and_clears_undo()
    {
        var rig = await new Rig().StartedAsync();
        rig.Device.Store.AddNode(Card());
        await rig.Session.SaveToCaseAsync();
        var id = rig.Device.Documents.CurrentServerId!.Value;
        var theirs = new CanvasDocument { CaseId = Case, Nodes = [Card("Theirs"), Card("Theirs too")] };
        rig.Server.Put(CanvasSerializer.Serialize(theirs), id, revision: 5);
        rig.Device.Store.AddNode(Card("Mine"));
        await rig.Session.SaveToCaseAsync();

        await rig.Session.TakeTheirsAsync();

        Assert.Equal(["Theirs", "Theirs too"], rig.Device.Store.Document.Nodes.Select(n => ((CardData)n.Data).Title));
        Assert.Equal(5, rig.Device.Store.Document.Revision);
        Assert.False(rig.Device.Store.CanUndo);
        Assert.Equal(SaveStateKind.SavedServer, rig.Device.Documents.State.Kind);
    }

    [Fact]
    public async Task Opening_a_case_reuses_the_copy_on_this_device()
    {
        var rig = await new Rig().StartedAsync();
        rig.Device.Store.AddNode(Card());
        await rig.Session.SaveToCaseAsync();
        var id = rig.Device.Documents.CurrentServerId!.Value;
        var localId = rig.Device.Documents.CurrentLocalId;
        rig.Device.Documents.New(null);

        Assert.Null(await rig.Session.OpenForCaseAsync(Case, Org));

        Assert.Equal(id, rig.Device.Documents.CurrentServerId);
        Assert.Equal(localId, rig.Device.Documents.CurrentLocalId);
        Assert.Single(rig.Device.Documents.Documents, d => d.ServerId == id);
    }

    [Fact]
    public async Task An_unchanged_device_copy_catches_up_with_a_newer_case_board()
    {
        var rig = await new Rig().StartedAsync();
        rig.Device.Store.AddNode(Card());
        await rig.Session.SaveToCaseAsync();
        var id = rig.Device.Documents.CurrentServerId!.Value;
        var localId = rig.Device.Documents.CurrentLocalId;
        rig.Server.Put(CanvasSerializer.Serialize(new CanvasDocument { CaseId = Case, Nodes = [Card("Newer")] }), id, revision: 3);

        Assert.Null(await rig.Session.OpenForCaseAsync(Case, Org));

        Assert.Equal("Newer", ((CardData)Assert.Single(rig.Device.Store.Document.Nodes).Data).Title);
        Assert.Equal(3, rig.Device.Store.Document.Revision);
        // The newer copy replaces the device copy under its own key: never a second copy of the same board.
        Assert.Equal(localId, rig.Device.Documents.CurrentLocalId);
        Assert.Single(rig.Device.Documents.Documents, d => d.ServerId == id);
    }

    [Fact]
    public async Task A_changed_device_copy_and_a_newer_case_board_become_a_choice()
    {
        var rig = await new Rig().StartedAsync();
        rig.Device.Store.AddNode(Card());
        await rig.Session.SaveToCaseAsync();
        var id = rig.Device.Documents.CurrentServerId!.Value;
        rig.Server.Put(rig.Server.Boards[id].DocumentJson, id, revision: 3);
        rig.Device.Store.AddNode(Card("Unsaved here"));

        Assert.Equal(CanvasCopy.Sentences.ServerConflict, await rig.Session.OpenForCaseAsync(Case, Org));

        Assert.Equal(2, rig.Device.Store.Document.Nodes.Count);
        Assert.Equal(3, rig.Session.PendingConflict!.Revision);
    }

    [Fact]
    public async Task A_case_with_no_boards_starts_a_new_board_for_it()
    {
        var rig = new Rig();
        await rig.Device.StartedAsync();
        rig.Device.Documents.New(null);

        Assert.Null(await rig.Session.OpenForCaseAsync(Case, Org));

        Assert.Equal(Case, rig.Device.Store.Document.CaseId);
        Assert.Null(rig.Device.Documents.CurrentServerId);
    }

    /// <summary>
    /// A board somebody just started from a template is not replaced by the case's newest board.
    /// </summary>
    /// <remarks>
    /// This is the one seam templates could have broken silently. A new board from a template is
    /// unsaved, so opening the case afterwards — which every load does — would list the case's boards,
    /// find a newer one and open it, and the frames somebody picked a second earlier would be gone with
    /// no message. The case and the organisation are still adopted, so the first save still lands in
    /// the right place; only the OPENING is declined.
    /// </remarks>
    [Fact]
    public async Task A_board_just_started_from_a_template_is_not_replaced_by_the_cases_newest()
    {
        var rig = await new Rig().StartedAsync();
        rig.Device.Store.AddNode(Card());
        await rig.Session.SaveToCaseAsync();

        // A second board, started from a template a moment ago and not saved anywhere yet.
        rig.Device.Documents.New(Case, "deck");
        var localId = rig.Device.Documents.CurrentLocalId;

        Assert.Null(await rig.Session.OpenForCaseAsync(Case, Org, keepWhatIsOpen: true));

        Assert.Equal(4, rig.Device.Store.Document.Groups.Count);
        Assert.Equal(localId, rig.Device.Documents.CurrentLocalId);
        Assert.Null(rig.Device.Documents.CurrentServerId);
        Assert.Equal(Org, rig.Device.Documents.CurrentOrganizationId);
        Assert.True(rig.Access.CanEdit);
    }

    /// <summary>And without that, the case's newest board is what opens — the ordinary load.</summary>
    [Fact]
    public async Task Without_that_the_cases_newest_board_opens_as_it_always_has()
    {
        var rig = await new Rig().StartedAsync();
        rig.Device.Store.AddNode(Card());
        await rig.Session.SaveToCaseAsync();
        var id = rig.Device.Documents.CurrentServerId!.Value;

        rig.Device.Documents.New(Case, "deck");

        Assert.Null(await rig.Session.OpenForCaseAsync(Case, Org));

        Assert.Equal(id, rig.Device.Documents.CurrentServerId);
        Assert.Empty(rig.Device.Store.Document.Groups);
    }

    [Fact]
    public async Task Pictures_are_uploaded_before_the_board_and_once_only()
    {
        var rig = await new Rig().StartedAsync();
        var asset = Guid.NewGuid();
        rig.Device.Files.Add(asset, ".png", DateTime.UtcNow);
        rig.Device.Store.AddNode(new CanvasNode { Type = CanvasNodeType.Image, Width = 320, Height = 240, Data = new ImageData { AssetId = asset, OpfsExt = ".png" } });

        await rig.Session.SaveToCaseAsync();
        await rig.Session.SaveToCaseAsync();

        Assert.Equal(["upload", "save", "save"], rig.Server.Log);
        Assert.Equal($"blob:fake/{asset:D}.png", Assert.Single(rig.Media.Uploads).Source);
        Assert.NotNull(((ImageData)rig.Device.Store.Document.Nodes[0].Data).UploadFileId);
        Assert.Contains("uploadFileId", rig.Server.Saves[0].Json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A recording dropped on a board goes to the case with everything else.
    /// </summary>
    /// <remarks>
    /// Left out of the upload switch it would stay on the device that dropped it: the board saves,
    /// the author goes on hearing it because their own copy is still there, and everybody else
    /// opens a block with nothing in it. That is the worst shape a bug can take on an evidence
    /// board — invisible to the only person who could notice.
    /// </remarks>
    [Theory]
    [InlineData("evp-001.m4a")]
    [InlineData("porch.mp4")]
    public async Task A_recording_is_uploaded_to_the_case_like_any_other_file(string fileName)
    {
        var rig = await new Rig().StartedAsync();
        var asset = Guid.NewGuid();
        var ext = Path.GetExtension(fileName);
        rig.Device.Files.Add(asset, ext, DateTime.UtcNow);

        NodeData data = fileName.EndsWith(".m4a", StringComparison.Ordinal)
            ? new AudioData { AssetId = asset, OpfsExt = ext, FileName = fileName }
            : new VideoData { AssetId = asset, OpfsExt = ext, FileName = fileName };
        var type = data is AudioData ? CanvasNodeType.Audio : CanvasNodeType.Video;

        rig.Device.Store.AddNode(new CanvasNode { Type = type, Width = 340, Height = 132, Data = data });

        await rig.Session.SaveToCaseAsync();

        Assert.Equal(fileName, Assert.Single(rig.Media.Uploads).Name);

        // And the block now points at the case's copy, or the next person to open the board would
        // be told the recording is not there.
        var saved = rig.Device.Store.Document.Nodes[0].Data;
        Assert.NotNull(saved is AudioData a ? a.UploadFileId : ((VideoData)saved).UploadFileId);
    }

    [Fact]
    public async Task A_failed_upload_stops_the_save_and_says_which_file()
    {
        var rig = await new Rig().StartedAsync();
        var asset = Guid.NewGuid();
        rig.Device.Files.Add(asset, ".png", DateTime.UtcNow);
        rig.Device.Store.AddNode(new CanvasNode { Type = CanvasNodeType.File, Width = 260, Height = 72, Data = new FileData { AssetId = asset, OpfsExt = ".png", FileName = "floor-plan.pdf" } });
        rig.Media.Refuse = "This case has used its storage allowance.";

        var problem = await rig.Session.SaveToCaseAsync();

        Assert.Equal(CanvasCopy.Sentences.FileNotUploaded("floor-plan.pdf", "This case has used its storage allowance."), problem);
        Assert.Empty(rig.Server.Saves);
        Assert.Equal(SaveStateKind.ServerFailed, rig.Device.Documents.State.Kind);
    }

    [Fact]
    public async Task A_refused_save_makes_the_board_view_only()
    {
        var rig = await new Rig().StartedAsync();
        rig.Device.Store.AddNode(Card());
        rig.Server.NextRefusal = new CanvasSaveResult(CanvasSaveOutcome.Failed, null, CanvasCopy.Sentences.SaveForbidden, Forbidden: true);

        Assert.Equal(CanvasCopy.Sentences.SaveForbidden, await rig.Session.SaveToCaseAsync());

        Assert.False(rig.Access.CanEdit);
        Assert.False(rig.Session.CanSaveToCase);
    }

    [Fact]
    public async Task A_board_the_server_marks_read_only_opens_view_only()
    {
        var rig = await new Rig().StartedAsync();
        rig.Server.CanEdit = false;
        var board = rig.Server.Put(CanvasSerializer.Serialize(new CanvasDocument { CaseId = Case, Nodes = [Card()] }));

        Assert.Null(await rig.Session.OpenAsync(board.Id));

        Assert.False(rig.Access.CanEdit);
        Assert.Equal(CanvasCopy.Sentences.ViewOnly, await rig.Session.SaveToCaseAsync());
        Assert.Empty(rig.Server.Saves);
    }

    [Fact]
    public async Task A_board_gone_from_the_server_is_created_again_on_the_next_save()
    {
        var rig = await new Rig().StartedAsync();
        rig.Device.Store.AddNode(Card());
        await rig.Session.SaveToCaseAsync();
        rig.Server.NextRefusal = new CanvasSaveResult(CanvasSaveOutcome.Failed, null, CanvasCopy.Sentences.BoardGoneFromServer);

        await rig.Session.SaveToCaseAsync();
        rig.Server.NextRefusal = null;
        await rig.Session.SaveToCaseAsync();

        Assert.Null(rig.Server.Saves[^1].ExistingId);
    }

    [Fact]
    public async Task When_the_server_cleans_the_board_its_copy_is_opened()
    {
        var rig = await new Rig().StartedAsync();
        rig.Device.Store.AddNode(Card("Before"));
        rig.Server.Rewrite = json => json.Replace("Before", "Cleaned", StringComparison.Ordinal);

        await rig.Session.SaveToCaseAsync();

        Assert.Equal("Cleaned", ((CardData)rig.Device.Store.Document.Nodes[0].Data).Title);
    }

    [Fact]
    public async Task Publish_saves_first_then_files_the_picture_of_what_was_saved()
    {
        var rig = await new Rig().StartedAsync();
        var asset = Guid.NewGuid();
        rig.Device.Files.Add(asset, ".png", DateTime.UtcNow);
        rig.Device.Store.AddNode(new CanvasNode { Type = CanvasNodeType.Image, Width = 320, Height = 240, Data = new ImageData { AssetId = asset, OpfsExt = ".png" } });
        Core.Persistence.SnapshotScene? drawn = null;

        var problem = await rig.Session.PublishAsync(scene =>
        {
            drawn = scene;
            rig.Server.Log.Add("draw");
            return Task.FromResult<byte[]?>([0x89, 0x50, 0x4E, 0x47]);
        });

        Assert.Null(problem);
        Assert.Equal(["upload", "save", "draw", "publish"], rig.Server.Log);
        Assert.Equal($"blob:fake/{asset:D}.png", Assert.Single(drawn!.Blocks).ImageUrl);
        Assert.Equal(rig.Device.Documents.CurrentServerId, rig.Server.Published);
        Assert.NotNull(rig.Device.Store.Document.PublishedAtUtc);
        Assert.Equal(SaveStateKind.SavedServer, rig.Device.Documents.State.Kind);
    }

    [Fact]
    public async Task A_picture_that_could_not_be_drawn_publishes_nothing()
    {
        var rig = await new Rig().StartedAsync();
        rig.Device.Store.AddNode(Card());

        var problem = await rig.Session.PublishAsync(_ => Task.FromResult<byte[]?>(null));

        Assert.Equal(CanvasCopy.Sentences.PublishFailed, problem);
        Assert.Null(rig.Server.Published);
        Assert.Equal(SaveStateKind.ServerFailed, rig.Device.Documents.State.Kind);
    }

    [Fact]
    public async Task A_conflict_stops_publish_before_the_picture_is_drawn()
    {
        var rig = await new Rig().StartedAsync();
        rig.Device.Store.AddNode(Card());
        await rig.Session.SaveToCaseAsync();
        var id = rig.Device.Documents.CurrentServerId!.Value;
        rig.Server.Put(rig.Server.Boards[id].DocumentJson, id, revision: 9);
        rig.Device.Store.AddNode(Card("Mine"));
        var drew = false;

        var problem = await rig.Session.PublishAsync(_ => { drew = true; return Task.FromResult<byte[]?>([1]); });

        Assert.Equal(CanvasCopy.Sentences.NewerServerCopy(9), problem);
        Assert.False(drew);
        Assert.Null(rig.Server.Published);
    }

    [Fact]
    public async Task Signed_out_nothing_is_sent()
    {
        var rig = await new Rig().StartedAsync();
        rig.Server.Available = false;
        rig.Device.Store.AddNode(Card());

        Assert.Equal(CanvasCopy.Sentences.SignedOutSave, await rig.Session.SaveToCaseAsync());
        Assert.Empty(rig.Server.Saves);
    }
}
