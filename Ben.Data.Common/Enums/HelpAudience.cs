namespace Ben.Data.Common.Enums;

/// <summary>
/// Who a help document is written for. Ordered from most public to most privileged.
/// </summary>
/// <remarks>
/// <para>This is a <b>visibility floor</b>, not a role. A reader sees every document at or below
/// what they have earned, so the set grows as someone takes on more — it never forks into a
/// separate copy per role. One document about cases, read by everyone who deals with cases, is
/// the point: two copies drift, and the one you are reading is always the stale one.</para>
///
/// <para>A document is therefore filed at the <i>lowest</i> audience that should see it. Filing
/// it higher than necessary hides it from people it would have helped.</para>
///
/// <para>Lives in the shared assembly because the WebApi decides a reader's ceiling (it has the
/// memberships) and the UI filters against it. Two definitions would eventually disagree.</para>
/// </remarks>
public enum HelpAudience
{
    /// <summary>Anyone, signed in or not.</summary>
    Everyone = 0,

    /// <summary>Anyone with an account.</summary>
    SignedIn = 1,

    /// <summary>Belongs to at least one organization, in any role.</summary>
    OrganizationMember = 2,

    /// <summary>Owner or Administrator of at least one organization.</summary>
    OrganizationAdministrator = 3,

    /// <summary>App-wide Admin or SuperAdmin.</summary>
    AppAdministrator = 4,
}
