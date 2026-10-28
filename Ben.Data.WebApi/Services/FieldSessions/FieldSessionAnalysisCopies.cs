using System.Security.Cryptography;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Billing;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.FieldSessions;

/// <summary>What making (or finding) a copy came to.</summary>
/// <param name="Copy">The copy, when there is one.</param>
/// <param name="Created">True when this call made it.</param>
/// <param name="Status">The HTTP status a refusal should carry: 404, 409 or 413.</param>
/// <param name="Problem">The sentence, when refused.</param>
public sealed record AnalysisCopyOutcome(UploadFile? Copy, bool Created, int Status, string? Problem);

/// <summary>
/// Copies one recording out of a field session into the scanner's files, or their group's, so it
/// can be scanned and marked like any other recording (phase 9, 10/27/2026).
/// </summary>
/// <remarks>
/// <para><b>The session is only ever read.</b> The bytes come from the bundle through
/// <see cref="IBenBundleStore.OpenEntryAsync"/> (or the older per-file row) and are written to a new
/// path under the person or the group. Nothing writes to the session's own path.</para>
///
/// <para><b>Verbatim, and checked.</b> The copy is the phone's own .m4a, not a transcode: about 30 MB
/// an hour, and the scanner hears exactly what was recorded. It is hashed as it is written and
/// compared with the digest the phone sent; a copy that does not match is deleted and refused,
/// because markers on the wrong audio are worse than none.</para>
///
/// <para><b>Whose room it takes.</b> A person's copy counts against their own allowance, asked
/// before the bytes are written. A group's copy lives under the group and answers to its plan:
/// a lapsed subscription adds nothing new.</para>
/// </remarks>
public sealed class FieldSessionAnalysisCopies(
    IFileStorageService storage,
    IBenBundleStore bundles,
    IMediaIngestService media,
    SubscriptionLimitGuard limits,
    ILogger<FieldSessionAnalysisCopies> logger)
{
    /// <summary>The file type session recordings are filed under.</summary>
    public static readonly Guid EvidenceFileTypeId = new("20000000-0000-0000-0000-000000000001");

    public const string MismatchSentence = "The copy did not match the recording on file, so it was not kept. Try again; if it happens twice, tell us.";
    public const string GoneSentence = "That recording is no longer on the server.";

    /// <summary>This scope's copy of the recording, made now if there is none yet.</summary>
    public async Task<AnalysisCopyOutcome> EnsureAsync(
        BenDataContext db, FieldSessionUpload session, FieldSessionUploadFile recording, ScanScope scope,
        Guid userId, CancellationToken ct)
    {
        if (await FindAsync(db, recording.Id, scope, userId, ct) is { } existing)
            return new AnalysisCopyOutcome(existing, false, StatusCodes.Status200OK, null);

        var person = scope.Scope == AnalysisScope.Person;
        if (person)
        {
            if (await AccountStorageGuard.WhyCannotStoreAsync(db, userId, recording.FileSize, ct) is { } full)
                return new AnalysisCopyOutcome(null, false, StatusCodes.Status413PayloadTooLarge, full);
        }
        else if (await limits.WhyReadOnlyAsync(scope.OrganizationId!.Value, ct) is { } readOnly)
        {
            return new AnalysisCopyOutcome(null, false, StatusCodes.Status409Conflict, readOnly);
        }

        var extension  = Path.GetExtension(recording.RelativePath);
        var storedName = $"{Guid.NewGuid()}{extension}";
        var path = person
            ? storage.UserFilePath(userId, $"session-copies/{storedName}")
            : storage.OrgFilePath(scope.OrganizationId!.Value, $"session-copies/{storedName}");

        string digest;
        long length;
        var (source, verbatim) = await OpenRecordingAsync(db, session, recording, ct);
        await using (source)
        {
            if (source is null) return new AnalysisCopyOutcome(null, false, StatusCodes.Status404NotFound, GoneSentence);

            using var sha = SHA256.Create();
            await using var hashing = new CryptoStream(source, sha, CryptoStreamMode.Read, leaveOpen: true);
            await storage.WriteAsync(path, hashing, ct);
            if (!hashing.HasFlushedFinalBlock) await hashing.FlushFinalBlockAsync(ct);
            digest = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
            length = recording.FileSize;
        }

        // Compared only with a digest the recording itself matched on arrival: one that arrived
        // damaged would never match, and refusing to copy it would refuse it forever. A cleaned
        // copy (an older upload whose metadata was stripped for serving) differs by design.
        // A recording inside a .ben has no digest of its own row; the phone's seal, inside the
        // same unchanged file, says what it was (10/28/2026).
        var expected = recording.DigestMatched ? recording.Sha256 : null;
        if (expected is null && verbatim && recording.BundleEntryPath is { Length: > 0 } entryPath)
        {
            var bundlePath = await db.UploadFiles.AsNoTracking()
                .Where(f => f.Id == session.DocumentUploadFileId).Select(f => f.StoragePath).FirstOrDefaultAsync(ct);
            if (bundlePath is { Length: > 0 }
                && (await SessionSeal.ReadAsync(bundles, bundlePath, ct)).TryGetValue(entryPath, out var sealedEntry))
                expected = sealedEntry.Sha256;
        }
        if (verbatim && expected is { Length: > 0 }
            && !string.Equals(expected.Trim(), digest, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Copy of session {SessionId} file {FileId} did not match its digest", session.Id, recording.Id);
            await DeleteQuietlyAsync(path);
            return new AnalysisCopyOutcome(null, false, StatusCodes.Status409Conflict, MismatchSentence);
        }

        var now  = DateTime.UtcNow;
        var name = $"Session {session.StartedAt:MM-dd-yyyy} {Path.GetFileName(recording.RelativePath)}";
        var copy = new UploadFile
        {
            Id                         = Guid.NewGuid(),
            UploadFileTypeId           = EvidenceFileTypeId,
            AppUserId                  = person ? userId : null,
            OwnerOrganizationId        = person ? null : scope.OrganizationId,
            FileName                   = name,
            StoredFileName             = storedName,
            ContentType                = recording.ContentType ?? FieldSessionFileGuard.ContentTypeFor(recording.RelativePath),
            FileSize                   = length,
            StoragePath                = path,
            Description                = $"Copied from the field session {session.LocationLabel ?? "recording"} on "
                                       + $"{session.StartedAt:MM/dd/yyyy} to scan for EVPs. The session itself is unchanged.",
            SourceFieldSessionUploadId = session.Id,
            SourceClipPath             = recording.RelativePath,
            DateCreated                = now,
            CreatedByAppUserId         = userId,
        };
        db.UploadFiles.Add(copy);
        db.FieldSessionAnalysisCopies.Add(new FieldSessionAnalysisCopy
        {
            Id                       = Guid.NewGuid(),
            FieldSessionUploadId     = session.Id,
            FieldSessionUploadFileId = recording.Id,
            CopyUploadFileId         = copy.Id,
            Scope                    = scope.Scope,
            ScopeAppUserId           = person ? userId : null,
            ScopeOrganizationId      = person ? null : scope.OrganizationId,
            SourceSha256             = digest,
            SourceRelativePath       = recording.RelativePath,
            MadeByAppUserId          = userId,
            DateCreated              = now,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two opens at once: the other one's copy won the unique index. Use it.
            await DeleteQuietlyAsync(path);
            db.ChangeTracker.Clear();
            if (await FindAsync(db, recording.Id, scope, userId, ct) is { } winner)
                return new AnalysisCopyOutcome(winner, false, StatusCodes.Status200OK, null);
            throw;
        }

        return new AnalysisCopyOutcome(copy, true, StatusCodes.Status201Created, null);
    }

    /// <summary>This scope's copy of the recording, if one has been made.</summary>
    public static Task<UploadFile?> FindAsync(
        BenDataContext db, Guid recordingId, ScanScope scope, Guid userId, CancellationToken ct)
    {
        var copies = db.FieldSessionAnalysisCopies.AsNoTracking().Where(c => c.FieldSessionUploadFileId == recordingId);
        copies = scope.Scope == AnalysisScope.Person
            ? copies.Where(c => c.ScopeAppUserId == userId)
            : copies.Where(c => c.ScopeOrganizationId == scope.OrganizationId);
        return copies.Select(c => c.CopyUploadFile).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// The recording's bytes as the session's own player serves them, and whether they are the
    /// phone's bytes unchanged (so its digest applies).
    /// </summary>
    private async Task<(Stream? Stream, bool Verbatim)> OpenRecordingAsync(
        BenDataContext db, FieldSessionUpload session, FieldSessionUploadFile recording, CancellationToken ct)
    {
        if (recording.BundleEntryPath is { Length: > 0 } entryPath)
        {
            var bundlePath = await db.UploadFiles.AsNoTracking()
                .Where(f => f.Id == session.DocumentUploadFileId).Select(f => f.StoragePath).FirstOrDefaultAsync(ct);
            if (bundlePath is not { Length: > 0 } || !storage.Exists(bundlePath)) return (null, false);
            return (await bundles.OpenEntryAsync(bundlePath, entryPath, ct), true);
        }

        var recordingPath = await db.UploadFiles.AsNoTracking()
            .Where(f => f.Id == recording.UploadFileId).Select(f => f.StoragePath).FirstOrDefaultAsync(ct);
        if (recordingPath is not { Length: > 0 } || !storage.Exists(recordingPath)) return (null, false);
        // The cleaned copy, as the session's player serves it: the original can carry where the
        // phone was, and a group's copy reaches people the owner never showed that to.
        var served = media.ServingPathFor(recordingPath);
        return (await storage.OpenReadAsync(served, ct), served == recordingPath);
    }

    private async Task DeleteQuietlyAsync(string path)
    {
        try { await storage.DeleteAsync(path, CancellationToken.None); }
        catch (Exception ex) { logger.LogWarning(ex, "A refused session copy at {Path} was not deleted", path); }
    }
}
