namespace Ben.Service.Models.Entities;

/// <summary>
/// One of a user's profile photos. <see cref="IsPublic"/> names the slot (public or private),
/// <see cref="IsActive"/> says whether it is the one currently in use for that slot.
/// </summary>
public record AppUserPhotoRecord
{
    public Guid Id { get; init; }
    public Guid AppUserId { get; init; }
    public Guid UploadFileId { get; init; }
    public string? AltText { get; init; }
    public bool IsPublic { get; init; }
    public bool IsActive { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }

    /// <summary>Original file name, for listing prior photos the user might re-activate.</summary>
    public string? FileName { get; init; }
}

/// <summary>
/// The caller's own profile — the first self-service view of an account in the product. Only
/// the fields a user may change about themselves; role and membership stay admin-owned.
/// </summary>
public record MyProfileRecord
{
    public Guid AppUserId { get; init; }
    public string? DisplayName { get; init; }

    /// <summary>Legal first name. Required — the profile refuses to save it empty.</summary>
    public string? FirstName { get; init; }

    /// <summary>Legal last name. Required.</summary>
    public string? LastName { get; init; }

    public string? Email { get; init; }

    /// <summary>The active public photo, or null when none is set.</summary>
    public AppUserPhotoRecord? PublicPhoto { get; init; }

    /// <summary>The active private photo, or null when none is set.</summary>
    public AppUserPhotoRecord? PrivatePhoto { get; init; }

    /// <summary>
    /// This user's opt-in to showing their private photo to clients of the orgs they work for.
    /// One half of a two-key rule — see <see cref="AnyOrgAllowsPrivatePhotoSharing"/>.
    /// </summary>
    public bool SharePrivatePhotoWithClients { get; init; }

    /// <summary>
    /// Whether at least one org the user actively belongs to permits member private photos to be
    /// shown to clients. Read-only context for the profile page, so the opt-in toggle can say
    /// whether turning it on will actually do anything — an opt-in that silently does nothing
    /// because no org allows it is worse than no toggle at all.
    /// </summary>
    public bool AnyOrgAllowsPrivatePhotoSharing { get; init; }

    /// <summary>
    /// Self-declared, optional, and used for exactly one thing: which of the site's three
    /// default avatars stands in when this person has no photo (item 163). NotProvided is a
    /// first-class answer, not a gap — it selects the generic default.
    /// </summary>
    public Ben.Data.Common.Enums.ClientGender Gender { get; init; }
}

/// <param name="DisplayName">
/// Null leaves the current value alone; an empty or whitespace string clears it. The two are
/// distinguished deliberately so a partial update can't blank a name it never meant to touch.
/// </param>
/// <param name="SharePrivatePhotoWithClients">
/// Null leaves the current setting alone, so a caller updating only the display name can't
/// accidentally revoke or grant consent it never mentioned.
/// </param>
/// <param name="FirstName">Null leaves it unchanged; empty or whitespace is refused.</param>
/// <param name="LastName">Same.</param>
/// <param name="Gender">Null leaves it unchanged; NotProvided is a real choice that clears it.</param>
public sealed record UpdateMyProfileRequest(
    string? DisplayName,
    bool? SharePrivatePhotoWithClients = null,
    string? FirstName = null,
    string? LastName = null,
    Ben.Data.Common.Enums.ClientGender? Gender = null);

/// <param name="UploadFileId">An already-uploaded file to attach as a photo.</param>
/// <param name="IsPublic">Which slot to fill: true = public photo, false = private.</param>
/// <param name="AltText">Optional alt text for screen readers.</param>
public sealed record SetMyPhotoRequest(Guid UploadFileId, bool IsPublic, string? AltText);
