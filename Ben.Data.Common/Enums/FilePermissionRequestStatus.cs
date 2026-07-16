namespace Ben.Data.Common.Enums;

/// <summary>
/// Tracks the lifecycle state of a file-permission request stored on
/// <c>UploadFilePermissionRequest</c>.
/// </summary>
public enum FilePermissionRequestStatus
{
    /// <summary>The request has been submitted and is awaiting review by an authorised user.</summary>
    Pending = 0,

    /// <summary>The request was reviewed and the requested permissions were granted.</summary>
    Approved = 1,

    /// <summary>The request was reviewed and the requested permissions were refused.</summary>
    Denied = 2,

    /// <summary>The requesting user withdrew the request before it was acted upon.</summary>
    Cancelled = 3
}
