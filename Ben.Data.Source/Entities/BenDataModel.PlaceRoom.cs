using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A named space inside a place — Room 217, the cellar, the east wing.
    /// </summary>
    /// <remarks>
    /// <para><b>Why rooms exist (item 197).</b> A property whose reported haunting IS the
    /// attraction — a hotel, an inn, a dormitory — is described by its rooms, not by its address.
    /// "Room 217's history" is the thing a guest actually wants to read, and the thing an owner
    /// has to be able to write. Everywhere else on this site a place is somewhere a group VISITS;
    /// here it is somewhere a group RUNS, and that inversion starts with being able to name the
    /// parts of it.</para>
    ///
    /// <para><b>Half of this already exists on the phone.</b> The Field Kit records which room the
    /// operator says they are in and carries that label onto every reading and every capture until
    /// they change it (<c>ActiveFieldSession.room</c>, <c>roomsVisited</c>). Those labels are free
    /// text today because they had nowhere to land. This is where they land — a room a session can
    /// be attributed to rather than a string somebody retyped differently each night.</para>
    ///
    /// <para><b>Owned by an organization, not by the place.</b> A <c>Place</c> is shared: two
    /// groups can investigate the same building, and one is public data the other did not create.
    /// So rooms belong to the ORGANIZATION that defined them for a place, which means a hotel can
    /// describe its own building in detail without a visiting group's names appearing inside it,
    /// and without either being able to edit the other's. It also avoids inventing ownership on
    /// <c>Place</c>, which nothing else has needed and which would change what a place means
    /// everywhere it is already used.</para>
    /// </remarks>
    public class PlaceRoom : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>The group that defined this room. Rooms are per-group, per-place.</summary>
        public Guid OrganizationId { get; set; }

        public Guid PlaceId { get; set; }

        /// <summary>What it is called — "Room 217", "The cellar". Unique within a place.</summary>
        public string Name { get; set; } = null!;

        /// <summary>
        /// Which floor or wing it is on, as the property itself says it.
        /// </summary>
        /// <remarks>
        /// Free text rather than a number: buildings have a lobby level, a mezzanine, a basement
        /// and a "3½" more often than they have a tidy integer, and a property describing itself
        /// should not have to translate.
        /// </remarks>
        public string? Floor { get; set; }

        /// <summary>Anything worth saying about the room itself.</summary>
        public string? Description { get; set; }

        /// <summary>
        /// Whether this room may appear on the property's public page.
        /// </summary>
        /// <remarks>
        /// Defaults to false, and deliberately: the point of a haunted property is that its
        /// reports are the marketing, but a room is still a place people sleep in, and publishing
        /// one is a decision rather than a side effect of naming it. The same
        /// say-so-before-it-is-seen rule the rest of the site uses for anything with a person in
        /// it.
        /// </remarks>
        public bool IsPublic { get; set; }

        /// <summary>Where it sits in the list. Hand-ordered, because buildings are not alphabetical.</summary>
        public int SortOrder { get; set; }

        /// <summary>
        /// How many people sleep here, when the venue has said. Null means nobody has stated it.
        /// </summary>
        /// <remarks>
        /// Null and zero mean different things and both are real: null is "we have not said", and
        /// zero is "nobody sleeps in the chapel". A booking against a room with no stated capacity
        /// is allowed and simply cannot be refused for being over it, which is the right way round
        /// — a venue that has not filled this in should not find its own rooms unbookable.
        /// </remarks>
        public int? Capacity { get; set; }

        /// <summary>
        /// Whether an event may offer this room to guests.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="IsPublic"/>, which is about the property's public page. A
        /// staff room can be named, attributed to readings and kept off the page, and still never
        /// be somewhere a guest sleeps. Defaults to false for the same reason publishing does: a
        /// room becomes bookable because somebody said so, not because it exists.
        /// </remarks>
        public bool IsBookable { get; set; }

        /// <summary>What the beds actually are — "one double, two singles", "four bunks".</summary>
        /// <remarks>
        /// A number cannot answer "will the two of us have to share a bed", which is the question
        /// guests actually ask, so the count and the arrangement are kept apart.
        /// </remarks>
        public string? BedNote { get; set; }

        /// <summary>
        /// Retired rooms stay, so anything already attributed to them still reads.
        /// </summary>
        public bool IsActive { get; set; } = true;

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual Place Place { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
