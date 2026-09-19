using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// The one-paragraph readout for every session a report cites, keyed by session id.
/// </summary>
/// <remarks>
/// Read from each session's own document — the same file the player plays — so the report and
/// the playback cannot disagree. A document this server cannot open yields no entry, and the
/// citation then says so rather than inventing. Shared by the group's report endpoints and the
/// client's, which print the same PDF.
/// </remarks>
public static class CaseReportReadouts
{
    public static async Task<IReadOnlyDictionary<Guid, string?>> ForAsync(
        IEnumerable<CaseReportSectionFieldSession> citations, IFileStorageService storage, CancellationToken ct)
    {
        var result = new Dictionary<Guid, string?>();
        foreach (var session in citations.Select(f => f.FieldSessionUpload).Where(u => u is not null).DistinctBy(u => u.Id))
            result[session.Id] = await ForAsync(session, storage, ct);
        return result;
    }

    public static async Task<string?> ForAsync(FieldSessionUpload session, IFileStorageService storage, CancellationToken ct)
    {
        var file = session.DocumentUploadFile;
        string? document = null;
        if (file?.FileData is { Length: > 0 } inline) document = System.Text.Encoding.UTF8.GetString(inline);
        else if (!string.IsNullOrEmpty(file?.StoragePath))
        {
            try
            {
                await using var stream = await storage.OpenReadAsync(file.StoragePath, ct);
                if (stream is null) return null;   // storage had nothing to hand back
                using var reader = new StreamReader(stream);
                document = await reader.ReadToEndAsync(ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { document = null; }
        }
        return FieldSessionReadout.Compose(document)?.Sentence;
    }
}
