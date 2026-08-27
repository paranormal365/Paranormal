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
    /// <summary>
    /// A tour runs public tours by definition; an investigation group says so itself.
    /// </summary>
    /// <remarks>
    /// An event provider does NOT get this. It sells ticketed events, which the calendar already
    /// carries — flagging it as running walking tours would put it in the tours filter under false
    /// pretences, and the flag exists to answer exactly that question. It can still set the flag
    /// itself if it also walks people around, which is the whole point of the flag being separate
    /// from the kind.
    /// </remarks>
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
        => IsPublicFacing(kind)
            // "Where do we meet?" is the first thing anybody asks a tour, an event, or a hotel.
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
    public static bool EventsArePublicByDefault(OrganizationKind kind) => IsPublicFacing(kind);

    /// <summary>
    /// Whether this kind exists to be FOUND — its address and its calendar are the product.
    /// </summary>
    /// <remarks>
    /// Three kinds share this and one does not, which is the whole distinction the kind exists to
    /// draw. A tour, an event provider and a haunted property all sell a place and a date to the
    /// public; an investigation group's headquarters is frequently somebody's home and its
    /// calendar is mostly internal. Written once rather than repeated per default, so a kind added
    /// later cannot pick up half of the behaviour.
    /// </remarks>
    public static bool IsPublicFacing(OrganizationKind kind) => kind is
        OrganizationKind.GhostWalkingTour or
        OrganizationKind.PublicEventProvider or
        OrganizationKind.HauntedProperty;

    /// <summary>How a kind is named to a person.</summary>
    public static string DisplayName(OrganizationKind kind) => kind switch
    {
        OrganizationKind.GhostWalkingTour    => "Ghost walking tour",
        OrganizationKind.PublicEventProvider => "Paranormal events",
        OrganizationKind.HauntedProperty     => "Haunted property",
        _ => "Investigation group",
    };
}
