namespace Ben.Data.Common.Enums;

/// <summary>
/// What a room's scan keeps besides the model (LiDAR plan, phase 1).
/// </summary>
/// <remarks>
/// The choice is made on the phone before the upload, with the size of each shown beside it.
/// Append only; the numbers are stored.
/// </remarks>
public enum PlaceScanRetention : byte
{
    /// <summary>The model, the world map and the keyframe pictures — about 90–120 MB a room. The default.</summary>
    ModelAndPictures = 1,

    /// <summary>The model and the world map only — about 20–50 MB, for an account short of room.</summary>
    ModelOnly = 2,
}
