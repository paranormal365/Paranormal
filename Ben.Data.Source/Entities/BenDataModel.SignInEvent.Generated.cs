namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One sign-in attempt: who, when, and whether it worked.
    /// </summary>
    /// <remarks>
    /// <para>A table of its own rather than rows in <c>AuditLogs</c>. The dashboard's question is
    /// "how many people signed in each day", which is a <c>GROUP BY</c> over an indexed date
    /// column; answering it from the audit log would mean string-matching an action name across a
    /// mixed free-text stream that grows with every edit anyone makes, forever. It also lets
    /// sign-in history be pruned on its own schedule without touching the audit trail, which has
    /// different retention reasons.</para>
    ///
    /// <para>Deliberately <b>not</b> <c>IAuditableEntity</c>: these rows are already a record of
    /// who did what and when, so CreatedBy/UpdatedBy columns would restate the row's own subject.
    /// Nothing ever updates one — they are written once and read in aggregate.</para>
    ///
    /// <para><b>What is deliberately not here:</b> no IP address, no user agent, no location. The
    /// dashboard needs counts, and each of those fields would turn a counting table into a
    /// tracking one, with the retention and disclosure questions that follow. Add them only with a
    /// reason that survives being written down next to this paragraph.</para>
    /// </remarks>
    public partial class SignInEvent
    {
        /// <summary>
        /// The account that signed in — null when the attempt failed against an address that
        /// matches no account, since there is no user to point at.
        /// </summary>
        public Guid? AppUserId { get; set; }

        /// <summary>When the attempt happened. Indexed: every query here groups by day.</summary>
        public DateTime Utc { get; set; }

        /// <summary>
        /// Whether the attempt succeeded. Failures are kept because a rise in them is the signal
        /// that matters — a stream of failures against real accounts looks nothing like a stream
        /// of successes.
        /// </summary>
        public bool Succeeded { get; set; }

        /// <summary>
        /// How they signed in — "password" or "entra". Entra sign-ins never touch the password
        /// endpoint, so without this the two look identical in the totals and neither can be
        /// counted on its own.
        /// </summary>
        public string Method { get; set; } = null!;

        public virtual AppUser? AppUser { get; set; }
    }
}
