using System.Text.Json.Serialization;

namespace Ben.Service.Models.Entities;

/// <summary>
/// How a clip or an edit is saved. Both keep every sample exactly; neither throws anything away.
/// </summary>
/// <remarks>
/// <para>Ben, 10/26/2026: "I would not recommend a format that loses information." So there is no
/// MP3 here. MP3 drops the quietest sounds and what it judges masked, which is what an EVP search
/// listens for, and an .m4a turned into MP3 would lose twice.</para>
///
/// <para>FLAC is first, so it is the default for a request that does not say: the same samples as
/// the WAV at roughly half the size. A server without ffmpeg saves WAV instead, and the answer's
/// content type says which it was.</para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AudioSaveFormat>))]
public enum AudioSaveFormat
{
    Flac = 0,
    Wav  = 1,
}
