namespace Ben.Data.Common.Enums;

/// <summary>Types of access that can be requested for a shared file.</summary>
[Flags]
public enum FilePermissionType
{
    None    = 0,
    Use     = 1, // embed or reference in content
    Share   = 2, // re-share with others / different org
    Display = 4  // display publicly
}
