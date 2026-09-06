using System.Text.Json.Serialization;

namespace Ben.Service.Models.Entities;

/// <summary>A destructive audio-edit operation, applied server-side and saved as a new derived file.</summary>
/// <remarks>
/// <para><b>Named on the wire, and still numbered for anyone who was.</b> This API configures no
/// <c>JsonStringEnumConverter</c> anywhere, so every enum crosses as an integer — which is fine for
/// values nobody types, and was not fine here: the one enum a person or a script writes by hand.
/// <c>{"operation":"Normalize"}</c> was refused with "the JSON value could not be converted",
/// surfaced as "The request field is required", which reads as a missing field rather than as a
/// rejected one (2026-09-06 audio walk, finding R).</para>
///
/// <para>The converter is on this enum alone and accepts integers as well as names, so a caller
/// generated from the C# client keeps working unchanged while anyone reading the record definition
/// can send what it looks like it wants. Nothing persists these values, so the names are free to
/// be the contract.</para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AudioEditOperation>))]
public enum AudioEditOperation
{
    Cut,
    Silence,
    Normalize,
    Gain,
    Fade,
    Reverse,
    Speed,
    Pitch,
}

/// <summary>
/// Applies <see cref="Operation"/> to an existing upload-file's audio and saves the result as a new
/// <c>UploadFile</c>. <see cref="Start"/>/<see cref="End"/> are required for <c>Cut</c>/<c>Silence</c>;
/// <see cref="GainDb"/> for <c>Gain</c>; <see cref="FadeInSeconds"/>/<see cref="FadeOutSeconds"/> for <c>Fade</c>;
/// <see cref="SpeedRatio"/> for <c>Speed</c> (2.0 = twice as fast, 0.5 = half speed, pitch preserved);
/// <see cref="PitchSemitones"/> for <c>Pitch</c> (positive = up, negative = down, duration preserved).
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
    Guid               UploadFileTypeId,
    double?            SpeedRatio = null,
    double?            PitchSemitones = null);
