namespace Ben.Service.Models.Entities;

/// <summary>
/// A set of EVP scan settings somebody saved under their own name (phase 5, 10/26/2026).
/// </summary>
/// <param name="Problem">
/// Null when <paramref name="Options"/> can be used. Otherwise why not, in a sentence: a preset
/// saved before a range was tightened is shown with the reason instead of being run.
/// </param>
public sealed record EvpScanPresetRecord(
    Guid Id, string Name, EvpDetectionOptions Options, bool IsDefault, string? Problem = null);

/// <summary>Creates or replaces one of the caller's presets.</summary>
/// <param name="Name">1–60 characters, unique among the caller's own presets.</param>
/// <param name="IsDefault">Make this the preset a recording with no settings of its own starts from.</param>
public sealed record SaveEvpScanPresetRequest(string Name, EvpDetectionOptions Options, bool IsDefault = false);
