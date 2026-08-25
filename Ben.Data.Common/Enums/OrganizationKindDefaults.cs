namespace Ben.Data.Common.Enums;

/// <summary>
/// What a new organization starts with, decided by the kind it says it is
/// (ghost walking tours, 2026-08-24).
/// </summary>
/// <remarks>
/// <para><b>Defaults, not rules.</b> Every value here is a starting point the group can change
/// the moment it wants to. The point is that the starting point should be the one that kind of
/// group almost always wants: an investigation group's headquarters is often somebody's home
/// and starts hidden, while a tour's meeting point is the product and starts shown. Making a
/// tour operator undo privacy they never asked for is how a good default becomes a support
/// ticket.</para>
///
/// <para><b>It lives in Ben.Data.Common on purpose.</b> The creation wizard (website) fills the
/// form with these and the API (server) applies them to the entity — two callers, and if they
/// disagreed about what "a new tour" means the difference would show up as a tour whose
/// address is quietly private. One definition, both sides.</para>
/// </remarks>
public static class OrganizationKindDefaults
{
    /// <summary>A tour runs public tours by definition; an investigation group says so itself.</summary>
    public static bool RunsPublicTours(OrganizationKind kind)
        => kind == OrganizationKind.GhostWalkingTour;

    /// <summary>
    /// How a new address should default for this kind. The investigation-group answer is the
    /// pre-existing one, unchanged: hidden from the public, findable only as a region.
    /// </summary>
    public static (OrganizationAddressVisibility Visibility,
                   OrganizationAddressDisplayMode PublicDisplayMode,
                   bool IsSearchable,
                   OrganizationAddressVisibility SearchVisibility) AddressDefaults(OrganizationKind kind)
        => kind == OrganizationKind.GhostWalkingTour
            // "Where do we meet?" is the first thing anybody asks a tour.
            ? (OrganizationAddressVisibility.Public,
               OrganizationAddressDisplayMode.FullAddressAndMap,
               true,
               OrganizationAddressVisibility.Public)
            : (OrganizationAddressVisibility.Private,
               OrganizationAddressDisplayMode.Hidden,
               true,
               OrganizationAddressVisibility.Public);

    /// <summary>
    /// Whether a new calendar event should default to public for this kind.
    /// </summary>
    /// <remarks>
    /// A tour's events ARE its product — defaulting them to members-only would hide the very
    /// thing the group joined to advertise. An investigation group's calendar is mostly
    /// internal, so it keeps the members-only default and opts a public event in.
    /// </remarks>
    public static bool EventsArePublicByDefault(OrganizationKind kind)
        => kind == OrganizationKind.GhostWalkingTour;

    /// <summary>How a kind is named to a person.</summary>
    public static string DisplayName(OrganizationKind kind) => kind switch
    {
        OrganizationKind.GhostWalkingTour => "Ghost walking tour",
        _ => "Investigation group",
    };
}
