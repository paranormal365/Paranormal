using Ben.Canvas.Core.Commands;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Persistence;
using Ben.Canvas.Core.Serialization;
using Ben.Canvas.Core.Text;
using Microsoft.Extensions.Logging;

namespace Ben.Canvas.Editor.Services;

/// <summary>
/// The open board and the case: Save to case, opening a case's board, and the choice when somebody else saved a
/// newer copy first.
/// </summary>
/// <remarks>
/// <para>The device copy always comes first. A save to the case writes the board on this device before it
/// sends anything, and a save that fails leaves that copy untouched, so no server answer can lose work.</para>
///
/// <para>Autosave never writes to the server. Saving to a case is something a person does, because it is what
/// other people on the case will see.</para>
///
/// <para>Pictures and files pasted onto the board are uploaded into the case's files before the board itself is
/// sent. If one cannot be uploaded the board is not sent, so the server never holds a block pointing at a file
/// it does not have.</para>
/// </remarks>
public sealed class CanvasServerSession(
    CanvasStore store,
    CanvasDocumentStore documents,
    CanvasAssetStore assets,
    ICanvasServerStore server,
    ICanvasMediaStore media,
    BoardAccess access,
    ILogger<CanvasServerSession> log)
{
    /// <summary>True when there is a server to talk to and the person is signed in.</summary>
    public bool IsServerAvailable => server.IsAvailable;

    /// <summary>True when a case board can be saved: a server, a signed-in person, a case, and edit rights.</summary>
    public bool CanSaveToCase => server.IsAvailable && store.Document.CaseId is not null && access.CanEdit;

    /// <summary>True while a save or open is on its way, so a second click does not send the board twice.</summary>
    public bool Busy { get; private set; }

    /// <summary>The newer server copy found when saving or opening, until the person chooses.</summary>
    public CanvasServerDocument? PendingConflict { get; private set; }

    public event Action? Changed;

    /// <summary>Saves the open board to its case. Returns a sentence when it did not land (a conflict included).</summary>
    public async Task<string?> SaveToCaseAsync(CancellationToken ct = default)
    {
        if (!server.IsAvailable) return CanvasCopy.Sentences.SignedOutSave;
        if (!access.CanEdit) return access.Reason ?? CanvasCopy.Sentences.ViewOnly;
        if (Busy) return null;

        Busy = true;
        Changed?.Invoke();
        try
        {
            await documents.SaveAsync();
            documents.SetState(new SaveState(SaveStateKind.SavingServer));

            if (await UploadFilesAsync(ct) is { } uploadProblem) return Fail(uploadProblem);

            var document = store.Document;
            var result = await server.SaveAsync(CanvasSerializer.Serialize(document, compact: true), documents.CurrentServerId, document.Revision, document.CaseId, ct);

            switch (result.Outcome)
            {
                case CanvasSaveOutcome.Saved when result.Document is { } saved:
                    PendingConflict = null;
                    await AdoptSavedAsync(saved);
                    access.Set(saved.CanEdit);
                    return null;

                case CanvasSaveOutcome.Conflict when result.Document is { } newer:
                    PendingConflict = newer;
                    documents.SetState(new SaveState(SaveStateKind.ServerConflict, Problem: result.Problem));
                    return result.Problem ?? CanvasCopy.Sentences.ServerConflict;

                default:
                    if (result.Forbidden) access.Set(false, CanvasCopy.Sentences.SaveForbidden);
                    if (result.Problem == CanvasCopy.Sentences.BoardGoneFromServer) documents.DetachFromServer();
                    return Fail(result.Problem ?? CanvasCopy.Sentences.ServerRefusedBoard);
            }
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Opens a board from the server by id. Returns a sentence when it cannot be opened.</summary>
    public async Task<string?> OpenAsync(Guid serverId, CancellationToken ct = default)
    {
        var (record, problem) = await server.GetAsync(serverId, ct);
        if (record is null) return problem;
        return await OpenRecordAsync(record);
    }

    /// <summary>
    /// Brings up the board for a case: the board already open here when it belongs to the case, otherwise the
    /// case's newest board (the copy on this device when there is one), otherwise a new board for the case.
    /// A newer server copy replaces an unchanged device copy, and becomes a conflict when both changed.
    /// </summary>
    public async Task<string?> OpenForCaseAsync(Guid caseId, Guid? organizationId, CancellationToken ct = default)
    {
        documents.SetOrganization(organizationId);
        var sameCase = store.Document.CaseId == caseId;

        if (!server.IsAvailable)
        {
            if (!sameCase && store.Document.Nodes.Count == 0) documents.New(caseId);
            return null;
        }

        if (sameCase && documents.CurrentServerId is { } openId)
            return await CatchUpAsync(openId, ct);

        var (boards, problem) = await server.ListAsync(caseId, ct);
        if (boards is null) return problem;

        var newest = boards.OrderByDescending(b => b.DateUpdated ?? b.DateCreated).FirstOrDefault();
        if (newest is null)
        {
            if (!sameCase) documents.New(caseId);
            documents.SetOrganization(organizationId);
            access.Set(true);
            return null;
        }

        if (documents.FindByServerId(newest.Id) is { } local && await documents.OpenLocalAsync(local.LocalId) is null)
            return await CatchUpAsync(newest.Id, ct);

        return await OpenAsync(newest.Id, ct);
    }

    /// <summary>
    /// Publishes the board: saves it to the case, draws its picture with <paramref name="draw"/>, and files the PNG
    /// in the case's files. Returns a sentence when any step did not land.
    /// </summary>
    /// <remarks>
    /// The picture is drawn from what was just saved, so the case never holds a picture of changes it does not have.
    /// Pictures on the board are drawn from this device's copy, or the case's when the device has none.
    /// </remarks>
    public async Task<string?> PublishAsync(Func<SnapshotScene, Task<byte[]?>> draw, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draw);
        if (!CanSaveToCase) return server.IsAvailable ? access.Reason ?? CanvasCopy.Sentences.SignedOutSave : CanvasCopy.Sentences.SignedOutSave;

        if (await SaveToCaseAsync(ct) is { } saveProblem) return saveProblem;
        if (documents.CurrentServerId is not { } serverId) return CanvasCopy.Sentences.ServerRefusedBoard;

        Busy = true;
        Changed?.Invoke();
        try
        {
            var scene = BoardSnapshot.Build(store.Document);
            var blocks = new List<SnapshotBlock>(scene.Blocks.Count);
            foreach (var block in scene.Blocks)
            {
                string? url = null;
                if (block.AssetId is { } asset) url = await assets.GetUrlAsync(asset, block.Ext);
                if (url is null && block.UploadFileId is { } upload) url = await media.GetDisplayUrlAsync(upload, thumbnail: false, ct);
                blocks.Add(url is null ? block : block with { ImageUrl = url });
            }

            var png = await draw(scene with { Blocks = blocks });
            if (png is null) return Fail(CanvasCopy.Sentences.PublishFailed);

            var (published, problem) = await server.PublishAsync(serverId, png, store.Document.Title, ct);
            if (published is null) return Fail(problem ?? CanvasCopy.Sentences.ServerRefusedBoard);

            // Not an edit: the board's content did not change, only when it was last published.
            store.Document.PublishedAtUtc = published.PublishedAtUtc;
            await documents.RecordServerSaveAsync(serverId, published.Revision, published.CaseId, published.OrganizationId);
            return null;
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Keeps this device's board: it is saved over the newer copy.</summary>
    public async Task<string?> KeepMineAsync(CancellationToken ct = default)
    {
        if (PendingConflict is not { } newer) return null;
        store.Document.Revision = newer.Revision;
        PendingConflict = null;
        return await SaveToCaseAsync(ct);
    }

    /// <summary>Takes the newer copy from the case; this device's changes are replaced (export first to keep them).</summary>
    public async Task<string?> TakeTheirsAsync()
    {
        if (PendingConflict is not { } newer) return null;
        return await OpenRecordAsync(newer);
    }

    private async Task<string?> CatchUpAsync(Guid serverId, CancellationToken ct)
    {
        var (record, problem) = await server.GetAsync(serverId, ct);
        if (record is null)
        {
            if (problem == CanvasCopy.Sentences.BoardGoneFromServer) documents.DetachFromServer();
            return problem;
        }

        access.Set(record.CanEdit);
        if (record.Revision <= store.Document.Revision) return null;

        if (documents.ChangedSinceServer)
        {
            PendingConflict = record;
            documents.SetState(new SaveState(SaveStateKind.ServerConflict, Problem: CanvasCopy.Sentences.NewerServerCopy(record.Revision)));
            Changed?.Invoke();
            return CanvasCopy.Sentences.ServerConflict;
        }

        return await OpenRecordAsync(record);
    }

    private async Task<string?> OpenRecordAsync(CanvasServerDocument record)
    {
        var (document, problem) = CanvasSerializer.Parse(record.DocumentJson);
        if (document is null) return problem;

        document.CaseId = record.CaseId ?? document.CaseId;
        await documents.OpenFromServerAsync(document, record.Id, record.Revision, record.OrganizationId);
        access.Set(record.CanEdit);
        PendingConflict = null;
        Changed?.Invoke();
        return null;
    }

    /// <summary>
    /// Takes on the server's answer. The server cleans message HTML on save, so when its copy differs from what was
    /// sent the server's copy is opened (undo starts again); otherwise only the revision changes.
    /// </summary>
    private async Task AdoptSavedAsync(CanvasServerDocument saved)
    {
        var current = store.Document;
        var (returned, _) = CanvasSerializer.Parse(saved.DocumentJson);

        if (returned is not null && !SameContent(current, returned))
        {
            log.LogInformation("The server changed the board while saving it (revision {Revision}); opening its copy.", saved.Revision);
            returned.CaseId = saved.CaseId ?? returned.CaseId;
            await documents.OpenFromServerAsync(returned, saved.Id, saved.Revision, saved.OrganizationId);
            return;
        }

        await documents.RecordServerSaveAsync(saved.Id, saved.Revision, saved.CaseId, saved.OrganizationId);
    }

    private static bool SameContent(CanvasDocument a, CanvasDocument b)
    {
        var (revA, revB, savedB) = (a.Revision, b.Revision, b.SavedAtUtc);
        try
        {
            a.Revision = b.Revision = 0;
            b.SavedAtUtc = a.SavedAtUtc;
            return CanvasSerializer.Serialize(a, compact: true) == CanvasSerializer.Serialize(b, compact: true);
        }
        finally
        {
            a.Revision = revA;
            b.Revision = revB;
            b.SavedAtUtc = savedB;
        }
    }

    /// <summary>Uploads every picture and file on the board that the case does not have yet.</summary>
    private async Task<string?> UploadFilesAsync(CancellationToken ct)
    {
        var document = store.Document;
        if (document.CaseId is not { } caseId || documents.CurrentOrganizationId is not { } organizationId || !media.IsAvailable)
            return null;

        foreach (var node in document.Nodes.ToList())
        {
            var (assetId, ext, uploaded, name) = node.Data switch
            {
                ImageData i => (i.AssetId, i.OpfsExt, i.UploadFileId, $"board-image-{i.AssetId:N}{i.OpfsExt}"),
                FileData f => (f.AssetId, f.OpfsExt, f.UploadFileId, string.IsNullOrWhiteSpace(f.FileName) ? $"board-file-{f.AssetId:N}{f.OpfsExt}" : f.FileName),
                _ => (null, null, null, ""),
            };
            if (assetId is not { } id || uploaded is not null) continue;

            var source = await assets.GetUrlAsync(id, ext);
            if (source is null)
            {
                log.LogWarning("Picture {Asset} is not on this device, so it was not uploaded.", id);
                continue;
            }

            var (upload, problem) = await media.UploadAsync(organizationId, caseId, source, name, $"From board \"{document.Title}\"", ct);
            if (upload is null) return CanvasCopy.Sentences.FileNotUploaded(name, problem ?? CanvasCopy.Sentences.ServerRefusedBoard);

            store.SetNodeDataWithoutHistory(node.Id, data =>
            {
                if (data is ImageData image) image.UploadFileId = upload.UploadFileId;
                else if (data is FileData file) file.UploadFileId = upload.UploadFileId;
            });
        }

        return null;
    }

    private string Fail(string problem)
    {
        documents.SetState(new SaveState(SaveStateKind.ServerFailed, Problem: problem));
        return problem;
    }
}
