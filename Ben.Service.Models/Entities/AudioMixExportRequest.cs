namespace Ben.Service.Models.Entities;

/// <summary>One placed clip in a Phase E mixer export — offset plus its track's gain/pan/mute/solo.</summary>
public record MixTrackExportInput(
    Guid CaseFileId,
    double OffsetSeconds,
    double GainDb,
    double Pan,
    bool Muted,
    bool Solo);

/// <summary>Request body for exporting a multi-track mix down to a single audio file.</summary>
public record ExportAudioMixRequest(IReadOnlyList<MixTrackExportInput> Tracks);
