using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Somebody helping at one event, and what they may do (item 235 phase 7).
    /// </summary>
    /// <remarks>
    /// <para><b>The person on the door is not a person with billing rights.</b> A hotel gives the
    /// door to a weekend helper who is not a member of anything, and the only way to let them scan
    /// a pass until now was to make them somebody who could change the group's settings. This row
    /// says "for THIS event, this person may do these things", which is the shape the job actually
    /// has: a steward on Saturday is nobody on Sunday.</para>
    ///
    /// <para><b>One table, not two.</b> The plan had a staff row and a separate invite row carrying
    /// the same five flags. A staff member who has not accepted yet is not a different kind of
    /// thing — it is the same row without an account attached, which is exactly what
    /// <see cref="AppUserId"/> being null means. Two tables would have been two places for the
    /// flags to disagree, and the acceptance would have had to copy them across correctly for
    /// ever.</para>
    ///
    /// <para><b>Flags rather than a role enum.</b> Ben's own list — see bookings, decide, run the
    /// door, menus and dietary, files — is not a ladder: a kitchen manager sees the dietary sheet
    /// and never the door, and a steward is the other way round. A role name is kept as a LABEL
    /// because a rota needs one, and it grants nothing.</para>
    ///
    /// <para><b>It only ever adds.</b> Nothing here takes a permission away from somebody who has
    /// it through the group's own roles: an owner is not demoted by being handed a scanner. The
    /// access rules read this as another way to be allowed, never as the only way.</para>
    /// </remarks>
    public partial class HostedEventStaff : IAuditableEntity
    {
        public Guid Id { get; set; }

        public Guid HostedEventId { get; set; }

        /// <summary>
        /// The account this is, once there is one. Null while an invitation is out.
        /// </summary>
        /// <remarks>
        /// Null is what makes an unaccepted invitation grant nothing: every access check matches on
        /// this column, and nobody's id is null.
        /// </remarks>
        public Guid? AppUserId { get; set; }

        /// <summary>Where the invitation went. Null for a member added directly.</summary>
        public string? Email { get; set; }

        /// <summary>What to call them before there is an account to take a name from.</summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// What they are doing — "Door", "Kitchen", "Guide". A label, not a permission.
        /// </summary>
        /// <remarks>
        /// A rota needs a word for what somebody is doing, and it must not be the same word the
        /// access rules read: a venue that calls somebody "Manager" has not thereby given them the
        /// booking board.
        /// </remarks>
        public string? RoleLabel { get; set; }

        /// <summary>See the board, with guests' names and dietary notes.</summary>
        public bool SeesBookings { get; set; }

        /// <summary>Confirm, turn down and release bookings.</summary>
        public bool Decides { get; set; }

        /// <summary>Scan passes and mark people in and out.</summary>
        public bool RunsTheDoor { get; set; }

        /// <summary>Read and write the menus, and read the kitchen's sheet.</summary>
        public bool SeesMenus { get; set; }

        /// <summary>Read the event's files.</summary>
        /// <remarks>
        /// Ahead of the files themselves, which are phase 11. Recorded now because the flag is part
        /// of the invitation somebody accepts, and adding a sixth switch later would mean asking
        /// every venue to revisit every helper.
        /// </remarks>
        public bool SeesFiles { get; set; }

        /// <summary>Single-use, cleared on acceptance so a forwarded email cannot be replayed.</summary>
        public string? Token { get; set; }

        public DateTime? DateExpires { get; set; }

        /// <summary>
        /// When they became real. Set immediately for a member added by hand.
        /// </summary>
        /// <remarks>
        /// Kept separate from <see cref="AppUserId"/> so a screen can tell "invited last Tuesday
        /// and never answered" from "joined last Tuesday", which are different conversations.
        /// </remarks>
        public DateTime? DateConfirmed { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEvent HostedEvent { get; set; } = null!;
        public virtual AppUser? AppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
