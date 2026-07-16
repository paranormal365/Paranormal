namespace Ben.Data.Common.Enums;

/// <summary>Controls who can see a file shared with an organization.</summary>
public enum FileShareVisibility
{
    OrgAdminsOnly = 0,
    OrgMembers    = 1,
    Public        = 2
}
