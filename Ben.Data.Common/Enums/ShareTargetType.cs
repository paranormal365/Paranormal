namespace Ben.Data.Common.Enums;

/// <summary>Who a <see cref="ShareTargetType"/>-tagged <c>UploadFileShare</c> row grants access to.</summary>
public enum ShareTargetType
{
    /// <summary>A specific person — <c>TargetAppUserId</c> is set.</summary>
    Person = 0,

    /// <summary>Everyone on one investigation's attendee roster — <c>TargetInvestigationId</c> is set.</summary>
    InvestigationTeam = 1,

    /// <summary>Every member of an organization — <c>TargetOrganizationId</c> is set.</summary>
    Organization = 2,

    /// <summary>Anyone — no target field is set.</summary>
    Public = 3,
}
