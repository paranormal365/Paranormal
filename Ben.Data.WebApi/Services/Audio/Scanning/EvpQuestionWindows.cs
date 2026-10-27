using System.Text.Json;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.FieldSessions;
using Ben.Service.Models.Entities;
using Ben.Service.Models.FieldSessions;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Audio.Scanning;

/// <summary>
/// Which question was hanging in the air when a candidate was heard (phase 9, 10/27/2026).
/// </summary>
/// <remarks>
/// <para>The phone marks an EVP session's questions (<c>evp_question</c>, with the question as its
/// note) and the end of each wait (<c>evp_wait_end</c>). A question's window runs from the question to
/// the first end of waiting, or to the next question if that comes first, or a minute when neither
/// does. A candidate starting inside one is noted "Q: …", so the reviewer hears it as an answer to
/// something, which is how investigators listen.</para>
///
/// <para>The recording's place in the session comes from the timeline's own clip placement (the
/// replay rules M4/M5). A recording the timeline could not place gets no notes, rather than wrong ones.</para>
/// </remarks>
public static class EvpQuestionWindows
{
    /// <summary>How long a question with no end of waiting and no next question is listened for.</summary>
    public const long DefaultWindowMs = 60_000;

    public sealed record Window(long FromMs, long ToMs, string Note);

    /// <summary>The question windows in the session, in time order.</summary>
    public static IReadOnlyList<Window> Of(SessionTimeline timeline)
    {
        var marks = timeline.Markers.OrderBy(m => m.T).ToList();
        var windows = new List<Window>();
        for (var i = 0; i < marks.Count; i++)
        {
            var question = marks[i];
            if (question.Kind != "evp_question") continue;

            long? end = null;
            for (var j = i + 1; j < marks.Count && end is null; j++)
                if (marks[j].Kind is "evp_wait_end" or "evp_question") end = marks[j].T;

            var note = string.IsNullOrWhiteSpace(question.Note)
                ? $"After the question at {Elapsed(question.T)}"
                : $"Q: {question.Note.Trim()}";
            windows.Add(new Window(question.T, end ?? question.T + DefaultWindowMs, note));
        }
        return windows;
    }

    /// <summary>
    /// The note for each candidate in this recording, or null when the recording asked nothing or
    /// cannot be placed in the session.
    /// </summary>
    public static Func<AudioCandidateRequest, string?>? For(SessionTimeline timeline, Guid recordingId)
    {
        var clip = timeline.Clips.FirstOrDefault(c => c.FileId == recordingId);
        var startMs = clip?.StartMs
            ?? (clip is { NotedEndMs: { } ended, DurationMs: { } length } ? ended - length : null);
        if (startMs is not { } offset) return null;

        var windows = Of(timeline);
        if (windows.Count == 0) return null;

        return candidate =>
        {
            var at = offset + (long)Math.Round(candidate.StartSeconds * 1000);
            return windows.LastOrDefault(w => w.FromMs <= at && at < w.ToMs)?.Note;
        };
    }

    private static string Elapsed(long ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }
}

/// <summary>
/// Finds the question windows for a recording copied out of a field session, so a scan of the copy
/// notes its candidates (phase 9).
/// </summary>
/// <remarks>
/// Reads the session's timeline through <see cref="FieldSessionTimelineService"/>, the cache every
/// reader of the session shares. Never fails a scan: a session that is gone or cannot be read means
/// no notes, and the candidates are proposed all the same.
/// </remarks>
public sealed class EvpQuestionContext(
    IDbContextFactory<BenDataContext> dbContextFactory,
    FieldSessionTimelineService timelines,
    ILogger<EvpQuestionContext> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Func<AudioCandidateRequest, string?>?> ForAsync(Guid copyUploadFileId, CancellationToken ct)
    {
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            var copy = await db.FieldSessionAnalysisCopies.AsNoTracking()
                .FirstOrDefaultAsync(c => c.CopyUploadFileId == copyUploadFileId, ct);
            if (copy is null) return null;

            var session = await db.FieldSessionUploads.AsNoTracking()
                .Include(s => s.Files).ThenInclude(f => f.UploadFile)
                .Include(s => s.DocumentUploadFile)
                .FirstOrDefaultAsync(s => s.Id == copy.FieldSessionUploadId, ct);
            if (session?.DocumentUploadFile.StoragePath is not { } documentPath) return null;

            // The readers' timeline, so a scan reuses what anybody watching the session already built.
            var payload = await timelines.GetAsync(new TimelineRequest(
                session.Id, session.IsBundle, documentPath, session.DocumentUploadFileId,
                session.DocumentUploadFile.DateUpdated ?? session.DocumentUploadFile.DateCreated,
                FieldSessionTimelineService.Sources(session.Files),
                TimelinePositionPolicy.Exact, Redact: null, Variant: "readers"), ct);
            if (payload is null) return null;

            var timeline = JsonSerializer.Deserialize<SessionTimeline>(payload.Json, Json);
            return timeline is null ? null : EvpQuestionWindows.For(timeline, copy.FieldSessionUploadFileId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The questions for copy {FileId} could not be read; its candidates get no notes", copyUploadFileId);
            return null;
        }
    }
}
