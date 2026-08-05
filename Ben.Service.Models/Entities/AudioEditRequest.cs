namespace Ben.Service.Models.Entities;

/// <summary>A destructive audio-edit operation, applied server-side and saved as a new derived file.</summary>
public enum AudioEditOperation
{
    Cut,
    Silence,
    Normalize,
    Gain,
    Fade,
    Reverse,
}

/// <summary>
/// Applies <see cref="Operation"/> to an existing upload-file's audio and saves the result as a new
/// <c>UploadFile</c>. <see cref="Start"/>/<see cref="End"/> are required for <c>Cut</c>/<c>Silence</c>;
/// <see cref="GainDb"/> for <c>Gain</c>; <see cref="FadeInSeconds"/>/<see cref="FadeOutSeconds"/> for <c>Fade</c>.
/// </summary>
public record AudioEditRequest(
    AudioEditOperation Operation,
    double?            Start,
    double?            End,
    double?            GainDb,
    double?            FadeInSeconds,
    double?            FadeOutSeconds,
    string?            Label,
    bool               IsPublic,
    Guid               UploadFileTypeId);
