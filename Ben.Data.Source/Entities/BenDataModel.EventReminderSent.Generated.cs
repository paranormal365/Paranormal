namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A record that one person has been reminded about one event.
    /// </summary>
    /// <remarks>
    /// <para>This table exists to make the reminder job idempotent, and that is its whole job. The
    /// scheduler wakes every few minutes and asks which events start soon; without a marker it
    /// would find the same event on every pass and send the same person the same email a dozen
    /// times before the evening. The unique index across
    /// <see cref="OrgCalendarEventId"/> and <see cref="AppUserId"/> is therefore not a tidiness
    /// constraint — it is the mechanism. A second insert for the same pair fails at the database
    /// rather than depending on the job having read the right rows a moment earlier, which is what
    /// makes it safe if two instances ever run at once.</para>
    ///
    /// <para>The marker is written <b>after</b> a successful send, so a send that throws is
    /// retried on the next pass. The failure mode that leaves is a duplicate email if the marker
    /// insert fails after the mail has gone; the opposite ordering risks silence, and being
    /// reminded twice is a great deal better than not being reminded at all.</para>
    ///
    /// <para>Rows are never updated and never read individually — only joined against to exclude
    /// people already told. They can be pruned once the event they name is well past.</para>
    /// </remarks>
    public partial class EventReminderSent
    {
        /// <summary>The event the reminder was about.</summary>
        public Guid OrgCalendarEventId { get; set; }

        /// <summary>The person who was reminded.</summary>
        public Guid AppUserId { get; set; }

        /// <summary>When the reminder went out.</summary>
        public DateTime SentUtc { get; set; }

        public virtual OrgCalendarEvent OrgCalendarEvent { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
    }
}
