namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A running tally of how often one rate limit has turned requests away.
    /// </summary>
    /// <remarks>
    /// <para><b>Ben's rule (2026-08-27):</b> <i>"Doesn't matter if it is 50,000 times… one time
    /// letting me know is enough. Then just a place to track more than the one time. So, 650 takes
    /// less of a look than 6,500."</i> A limit under real pressure refuses continuously, so
    /// repeating the message would only teach the reader to ignore the bell. One message per limit,
    /// ever — and this row is the place to look afterwards, where the magnitude is the whole point:
    /// the difference between 650 and 6,500 is the difference between a busy night and something
    /// that needs attention today, and neither is visible from a message that was sent once.</para>
    ///
    /// <para><b>One row per policy, not one per refusal.</b> An append-only log would be the
    /// obvious shape and the wrong one: 50,000 refusals is 50,000 inserts on the path that is
    /// already refusing work, to answer a question that only ever needs a total. Counts accumulate
    /// in memory and are flushed periodically into this row, so the busiest possible minute costs
    /// one update.</para>
    ///
    /// <para><b>What is approximate, and why that is fine.</b> <see cref="Refusals"/> is exact.
    /// <see cref="DistinctCallers"/> is the number of distinct addresses seen in the most recent
    /// flush window, not since the beginning — keeping a true lifetime distinct count would mean
    /// storing every address, which is both unbounded and personal data this table has no business
    /// holding. The question it answers is "is this a crowd or one script right now", and a recent
    /// window answers that better than a lifetime total would.</para>
    /// </remarks>
    public class RateLimitRefusal
    {
        public Guid Id { get; set; }

        /// <summary>The policy that did the refusing — see <c>RateLimiting</c>. Unique.</summary>
        public string PolicyName { get; set; } = null!;

        /// <summary>Total requests this limit has refused, across restarts.</summary>
        public long Refusals { get; set; }

        /// <summary>Distinct addresses refused in the most recent flush window.</summary>
        /// <remarks>
        /// Many addresses means real people are being turned away; one means a script is being
        /// held off, which is the limit working. This single number is the diagnosis.
        /// </remarks>
        public int DistinctCallers { get; set; }

        /// <summary>The most callers ever seen refused in one window — the worst moment so far.</summary>
        public int PeakDistinctCallers { get; set; }

        public DateTime DateFirstSeen { get; set; }

        public DateTime DateLastSeen { get; set; }

        /// <summary>
        /// When the one message about this limit was sent, or null if it has not been sent yet.
        /// </summary>
        /// <remarks>
        /// Nulling this re-arms the notice, which is what the "Notify me again" action on the
        /// admin page does — the only way a second message is ever sent for the same limit, and a
        /// deliberate act rather than a timer.
        /// </remarks>
        public DateTime? DateNotified { get; set; }
    }
}
