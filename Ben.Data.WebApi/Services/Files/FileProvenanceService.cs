using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.FieldSessions;
using Ben.Service.Models.FieldSessions;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Files;

/// <summary>One step from a session's recording to a file: what was done, with what settings, by whom.</summary>
public sealed record ProvenanceStep(
    string Op,
    Dictionary<string, double>? Settings,
    Guid By,
    DateTime At,
    string? ResultSha256);

/// <summary>
/// Certificates of authenticity for files made from a field session's recordings (10/28/2026).
/// </summary>
/// <remarks>
/// <para>Ben, 10/28/2026: an enhanced recording "should retain verification that it is from the
/// original, but enhanced... not AI or edited externally. So, like has a certificate of
/// authenticity." Every clip and edit made from a scanner's copy of a session recording — or from a
/// file already certified — is given one here, as the file is saved. A file made any other way has
/// none, and the page says so rather than implying one.</para>
///
/// <para><b>Enhanced</b> means only steps that keep the sound and its timing: the copy, a clip,
/// normalizing, gain, fades. Those are shown in the recording's place on the session's timeline.
/// Anything else (cut, silence, reverse, speed, pitch, a drawn volume curve) is certified as altered,
/// listed with the session, never played in the original's place.</para>
/// </remarks>
public sealed class FileProvenanceService(IBenBundleStore bundles, IFileStorageService storage, ProvenanceSigner signer)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Steps that keep the sound and its timing.</summary>
    private static readonly HashSet<string> Enhancing = new(StringComparer.Ordinal) { "copy", "clip", "normalize", "gain", "fade" };

    /// <summary>Steps after which the file no longer has one place in the recording.</summary>
    private static readonly HashSet<string> MovesTime = new(StringComparer.Ordinal) { "cut", "reverse", "speed" };

    public const string NoAiSentence =
        "Nothing was generated, and no AI changed the sound. A model may have helped find where to listen; it never touches the audio.";

    /// <summary>
    /// Adds the certificate for <paramref name="created"/>, made from <paramref name="parentId"/> by
    /// <paramref name="op"/>, when the parent is a scanner's copy or itself certified. Not saved here:
    /// it goes in with the file, or not at all. Returns null when the parent has no provenance.
    /// </summary>
    /// <param name="regionStartSeconds">Where in the parent the new file starts (a clip's start), else 0.</param>
    public async Task<FileProvenance?> RecordAsync(
        BenDataContext db, UploadFile created, Guid parentId, string op, Dictionary<string, double>? settings,
        double regionStartSeconds, byte[] resultBytes, long? durationMs, Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        now = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        var resultSha = Hash(resultBytes);
        var step = new ProvenanceStep(op, settings, userId, now, resultSha);

        FileProvenance row;
        if (await db.FileProvenances.AsNoTracking().FirstOrDefaultAsync(p => p.UploadFileId == parentId, ct) is { } parent)
        {
            var chain = Chain(parent.ChainJson);
            chain.Add(step);
            row = new FileProvenance
            {
                FieldSessionUploadId = parent.FieldSessionUploadId,
                AnalysisCopyUploadFileId = parent.AnalysisCopyUploadFileId,
                Scope = parent.Scope, ScopeAppUserId = parent.ScopeAppUserId, ScopeOrganizationId = parent.ScopeOrganizationId,
                SourceClipPath = parent.SourceClipPath,
                SealSha256 = parent.SealSha256,
                CopySha256 = parent.CopySha256,
                ChainJson = JsonSerializer.Serialize(chain, Json),
                RecordingOffsetMs = MovesTime.Contains(op) || parent.RecordingOffsetMs is null
                    ? null
                    : parent.RecordingOffsetMs + (long)Math.Round(regionStartSeconds * 1000),
                Enhanced = parent.Enhanced && Enhancing.Contains(op),
            };
        }
        else if (await db.FieldSessionAnalysisCopies.AsNoTracking().FirstOrDefaultAsync(c => c.CopyUploadFileId == parentId, ct) is { } copy)
        {
            var seal = await SealedHashAsync(db, copy, ct);
            var copied = new ProvenanceStep("copy", null, copy.MadeByAppUserId, copy.DateCreated, copy.SourceSha256);
            row = new FileProvenance
            {
                FieldSessionUploadId = copy.FieldSessionUploadId,
                AnalysisCopyUploadFileId = copy.CopyUploadFileId,
                Scope = copy.Scope, ScopeAppUserId = copy.ScopeAppUserId, ScopeOrganizationId = copy.ScopeOrganizationId,
                SourceClipPath = copy.SourceRelativePath,
                SealSha256 = seal,
                CopySha256 = copy.SourceSha256 ?? string.Empty,
                ChainJson = JsonSerializer.Serialize(new List<ProvenanceStep> { copied, step }, Json),
                RecordingOffsetMs = MovesTime.Contains(op) ? null : (long)Math.Round(regionStartSeconds * 1000),
                Enhanced = Enhancing.Contains(op),
            };
        }
        else
        {
            return null;
        }

        row.Id = Guid.NewGuid();
        row.UploadFileId = created.Id;
        row.ResultSha256 = resultSha;
        row.DurationMs = durationMs;
        row.MadeByAppUserId = userId;
        row.IssuedUtc = now;
        row.Signature = signer.Sign(Canonical(row));
        db.FileProvenances.Add(row);
        return row;
    }

    /// <summary>The certificate for a file, in sentences, or null when it has none.</summary>
    public static async Task<FileCertificateRecord?> DescribeAsync(BenDataContext db, Guid fileId, CancellationToken ct)
    {
        var row = await db.FileProvenances.AsNoTracking().FirstOrDefaultAsync(p => p.UploadFileId == fileId, ct);
        if (row is null) return null;

        var file = await db.UploadFiles.AsNoTracking().Where(f => f.Id == fileId)
            .Select(f => new { f.FileName }).FirstAsync(ct);
        var session = await db.FieldSessionUploads.AsNoTracking().Where(s => s.Id == row.FieldSessionUploadId)
            .Select(s => new { s.LocationLabel, s.StartedAt, s.RecordedByName, Sender = s.SubmittedByAppUser.DisplayName })
            .FirstOrDefaultAsync(ct);
        var people = Chain(row.ChainJson).Select(s => s.By).Append(row.MadeByAppUserId).Distinct().ToList();
        var names = await db.AppUsers.AsNoTracking().Where(u => people.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName ?? "Somebody", ct);

        var steps = Chain(row.ChainJson)
            .Select(s => new FileCertificateStep(Sentence(s, row), s.ResultSha256))
            .ToList();
        return new FileCertificateRecord(
            fileId, file.FileName, row.Enhanced,
            session?.LocationLabel ?? "Field session", session?.StartedAt ?? row.IssuedUtc, null,
            session?.RecordedByName ?? session?.Sender ?? "Somebody",
            row.SourceClipPath, row.SealSha256, row.CopySha256,
            row.SealSha256 is null ? null : string.Equals(row.SealSha256, row.CopySha256, StringComparison.OrdinalIgnoreCase),
            steps, row.ResultSha256, row.RecordingOffsetMs,
            names.GetValueOrDefault(row.MadeByAppUserId, "Somebody"), row.IssuedUtc, row.Signature is not null);
    }

    /// <summary>The file's bytes now against the certificate, and the certificate against the site's seal.</summary>
    public async Task<FileCertificateCheck?> CheckAsync(BenDataContext db, Guid fileId, CancellationToken ct)
    {
        var row = await db.FileProvenances.AsNoTracking().FirstOrDefaultAsync(p => p.UploadFileId == fileId, ct);
        if (row is null) return null;
        var path = await db.UploadFiles.AsNoTracking().Where(f => f.Id == fileId).Select(f => f.StoragePath).FirstOrDefaultAsync(ct);

        var current = "";
        if (path is { Length: > 0 } && storage.Exists(path))
        {
            await using var stream = await storage.OpenReadAsync(path, ct);
            current = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        }
        if (!string.Equals(current, row.ResultSha256, StringComparison.OrdinalIgnoreCase))
            return new FileCertificateCheck("changed",
                "This file no longer matches its certificate: its bytes are not the ones that were certified.", current);

        return signer.Verify(Canonical(row), row.Signature) switch
        {
            ProvenanceSigner.Check.Valid => new FileCertificateCheck("verified",
                "Verified just now: the file is byte for byte the one certified, and the certificate is the site's own, unchanged.", current),
            ProvenanceSigner.Check.Altered => new FileCertificateCheck("changed",
                "The file matches, but the certificate itself has been changed since the site issued it.", current),
            ProvenanceSigner.Check.Unsigned => new FileCertificateCheck("unsigned",
                "The file matches its certificate, which was issued without the site's seal.", current),
            _ => new FileCertificateCheck("unreadable",
                "The file matches its certificate, but the site's seal on it can no longer be read.", current),
        };
    }

    /// <summary>The steps in a few words, for a list: "Clip 12:40–12:47 · normalized".</summary>
    public static string Summary(string chainJson)
    {
        var parts = Chain(chainJson).Where(s => s.Op != "copy").Select(s => s.Op switch
        {
            "clip" => $"clip {Clock(Setting(s, "start"))}–{Clock(Setting(s, "end"))}" + (Setting(s, "normalize") > 0 ? " · normalized" : ""),
            "normalize" => "normalized",
            "gain" => $"{Signed(Setting(s, "gainDb"))} dB",
            "fade" => "faded",
            "cut" => "a stretch cut out",
            "silence" => "a stretch silenced",
            "reverse" => "reversed",
            "speed" => $"speed ×{Setting(s, "ratio"):0.##}",
            "pitch" => $"pitch {Signed(Setting(s, "semitones"))} semitones",
            "envelope" => "volume curve",
            _ => s.Op,
        });
        var text = string.Join(" · ", parts);
        return text.Length == 0 ? "Copy" : char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>Everything the seal covers, one field to a line, in a fixed order.</summary>
    internal static string Canonical(FileProvenance p) => string.Join('\n',
        "v1", p.UploadFileId, p.FieldSessionUploadId, p.AnalysisCopyUploadFileId, (int)p.Scope,
        p.ScopeAppUserId?.ToString() ?? "", p.ScopeOrganizationId?.ToString() ?? "",
        p.SourceClipPath, p.SealSha256 ?? "", p.CopySha256, p.ChainJson, p.ResultSha256,
        p.RecordingOffsetMs?.ToString(CultureInfo.InvariantCulture) ?? "", p.DurationMs?.ToString(CultureInfo.InvariantCulture) ?? "",
        p.Enhanced ? "1" : "0", p.MadeByAppUserId,
        // Written the same however the date comes back: SQL Server returns it with no zone, and "O"
        // then dropped the Z it was sealed with, so every certificate read back checked as changed.
        p.IssuedUtc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + "Z");

    internal static List<ProvenanceStep> Chain(string json)
        => JsonSerializer.Deserialize<List<ProvenanceStep>>(json, Json) ?? [];

    private async Task<string?> SealedHashAsync(BenDataContext db, FieldSessionAnalysisCopy copy, CancellationToken ct)
    {
        var session = await db.FieldSessionUploads.AsNoTracking().Where(s => s.Id == copy.FieldSessionUploadId)
            .Select(s => new { s.IsBundle, Path = s.DocumentUploadFile.StoragePath }).FirstOrDefaultAsync(ct);
        if (session is { IsBundle: true, Path: { Length: > 0 } bundlePath } && storage.Exists(bundlePath))
        {
            var sealedEntries = await SessionSeal.ReadAsync(bundles, bundlePath, ct);
            return sealedEntries.TryGetValue(copy.SourceRelativePath, out var entry) ? entry.Sha256 : null;
        }
        // A session sent as separate files: the digest the phone sent with the recording, when it matched.
        return await db.FieldSessionUploadFiles.AsNoTracking()
            .Where(f => f.Id == copy.FieldSessionUploadFileId && f.DigestMatched)
            .Select(f => f.Sha256).FirstOrDefaultAsync(ct);
    }

    private static string Sentence(ProvenanceStep s, FileProvenance row) => s.Op switch
    {
        "copy" => row.SealSha256 is null
            ? $"Copied out of the session's {Path.GetFileName(row.SourceClipPath)} unchanged. The session's seal couldn't be read to compare."
            : string.Equals(row.SealSha256, row.CopySha256, StringComparison.OrdinalIgnoreCase)
                ? $"Copied out of the session's {Path.GetFileName(row.SourceClipPath)} unchanged: it matches what the phone sealed when it saved the session."
                : $"Copied out of the session's {Path.GetFileName(row.SourceClipPath)}, but the copy does NOT match what the phone sealed.",
        "clip" => $"Cut from {Clock(Setting(s, "start"))} to {Clock(Setting(s, "end"))} of the recording"
                  + (Setting(s, "normalize") > 0 ? ", with its loudest point raised to −1 dB." : "."),
        "normalize" => "Normalized: its loudest point raised to −1 dB.",
        "gain" => Setting(s, "gainDb") >= 0 ? $"Made {Setting(s, "gainDb"):0.#} dB louder." : $"Made {-Setting(s, "gainDb"):0.#} dB quieter.",
        "fade" => $"Faded in over {Setting(s, "fadeIn"):0.##} s and out over {Setting(s, "fadeOut"):0.##} s.",
        "cut" => $"The stretch from {Clock(Setting(s, "start"))} to {Clock(Setting(s, "end"))} was cut out.",
        "silence" => $"The stretch from {Clock(Setting(s, "start"))} to {Clock(Setting(s, "end"))} was silenced.",
        "reverse" => "Reversed.",
        "speed" => $"Its speed changed to ×{Setting(s, "ratio"):0.##}.",
        "pitch" => $"Its pitch shifted by {Signed(Setting(s, "semitones"))} semitones.",
        "envelope" => "Its volume reshaped along a drawn curve.",
        _ => s.Op,
    };

    private static double Setting(ProvenanceStep s, string name) => s.Settings?.GetValueOrDefault(name) ?? 0;

    private static string Signed(double v) => v.ToString("+0.#;−0.#;0", CultureInfo.InvariantCulture);

    private static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
