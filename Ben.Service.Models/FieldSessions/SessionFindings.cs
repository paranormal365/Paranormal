namespace Ben.Service.Models.FieldSessions;

/// <summary>
/// What was found in a session's recordings, for its page (10/28/2026): the EVPs somebody kept, and
/// the clips made from the recordings — each enhanced one shown in its place on the timeline.
/// </summary>
/// <remarks>
/// Times are where in the RECORDING (ms); the page places them on the session with the recording's
/// own start from the timeline. Only what the person reading may open is listed: each scanner's
/// copy is their own, or their group's (Ben, 10/26/2026).
/// </remarks>
public sealed record SessionFindings(
    IReadOnlyList<SessionFindingClip> Clips,
    IReadOnlyList<SessionFindingEvp> Evps);

/// <summary>A clip or edit made from one of the session's recordings.</summary>
public sealed record SessionFindingClip(
    Guid FileId,
    string FileName,
    string ContentType,
    /// <summary>The session recording it came from, e.g. <c>media/audio-001.m4a</c>.</summary>
    string SourceClipPath,
    /// <summary>Where it starts in that recording; null when a step moved time (cut, speed, reverse).</summary>
    long? RecordingOffsetMs,
    long? DurationMs,
    /// <summary>Only our own steps that keep the sound and its timing: placed on the timeline.</summary>
    bool Enhanced,
    /// <summary>It has a certificate of authenticity.</summary>
    bool Certified,
    /// <summary>"Clip 12:40–12:47, normalized" — the steps in a few words.</summary>
    string Summary,
    string MadeBy,
    DateTime MadeUtc);

/// <summary>An EVP somebody kept on their copy of one of the session's recordings.</summary>
public sealed record SessionFindingEvp(
    Guid MarkerId,
    string SourceClipPath,
    long RecordingOffsetMs,
    long? RecordingEndMs,
    /// <summary>What they believe it says ("What did you hear?").</summary>
    string Heard,
    string Confidence,
    string? Note,
    string MadeBy,
    /// <summary>The clip saved from it, when one was.</summary>
    Guid? ClipFileId);

/// <summary>A certificate of authenticity, said in plain sentences.</summary>
public sealed record FileCertificateRecord(
    Guid FileId,
    string FileName,
    bool Enhanced,
    string SessionLabel,
    DateTime SessionStartedUtc,
    string? SessionTimeZoneId,
    string RecordedBy,
    string SourceClipPath,
    /// <summary>What the phone sealed for the recording; null when the seal could not be read.</summary>
    string? SealSha256,
    string CopySha256,
    /// <summary>The copy matched the phone's seal; null when there was no seal to check.</summary>
    bool? CopyMatchesSeal,
    IReadOnlyList<FileCertificateStep> Steps,
    string ResultSha256,
    long? RecordingOffsetMs,
    string MadeBy,
    DateTime IssuedUtc,
    bool Signed);

/// <summary>One step from the recording to the file.</summary>
public sealed record FileCertificateStep(string Sentence, string? ResultSha256);

/// <summary>A certificate checked now against the file's bytes and the site's seal.</summary>
public sealed record FileCertificateCheck(
    /// <summary><c>verified</c>, <c>changed</c>, <c>unsigned</c> or <c>unreadable</c>.</summary>
    string Outcome,
    string Sentence,
    string CurrentSha256);
