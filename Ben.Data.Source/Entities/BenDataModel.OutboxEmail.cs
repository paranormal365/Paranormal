namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One letter the site means to send, and everything that has happened to it (item 239).
    /// </summary>
    /// <remarks>
    /// <para><b>Why this table exists.</b> Ben signed up on 2026-08-31 and received nothing, and
    /// there was no way to find out why: the sender swallowed its own failure and the single log
    /// line recording it was a Warning, below the database sink's threshold, so the failure left no
    /// trace at all. <c>AdminMailDiagnosticsController</c> answers "can this machine send RIGHT
    /// NOW"; nothing answered "did THAT letter go, and if not, why, and will it be tried again".
    /// The answer to the last part was always no — every caller caught, logged and moved on.</para>
    ///
    /// <para>Ben, 2026-09-12: <i>"basically in order to verify all e-mails generated get sent and
    /// if it doesn't send on the first try it will try to send it on the next try."</i></para>
    ///
    /// <para><b>A row is written instead of a letter being sent.</b> <c>OutboxEmailService</c>
    /// decorates <see cref="Ben.Data.Common.Interfaces.IEmailService"/>, so all twenty callers —
    /// Identity's own password mail, the tour mailer, the event mailer, the reminder job, every
    /// invite door — were covered without one of them being edited.</para>
    /// </remarks>
    public partial class OutboxEmail
    {
        public Guid Id { get; set; }

        /// <summary>Where it is going. One recipient; nothing here sends to a list.</summary>
        public string To { get; set; } = null!;

        public string Subject { get; set; } = null!;

        /// <summary>
        /// The rendered HTML, or null once it has been scrubbed.
        /// </summary>
        /// <remarks>
        /// <para><b>Scrubbed, not kept for ever.</b> A body is personal data — somebody's name,
        /// what they booked, where they are staying — and for a hosted event it carries the pass
        /// QR, which is a working door credential. <see cref="BodyScrubbedUtc"/> is stamped when a
        /// sweep clears it, and the metadata beside it stays: "did it go" is answerable a year
        /// later, "what did it say" for a month.</para>
        ///
        /// <para>A scrubbed row can no longer be retried, which is the honest cost and the reason
        /// the window is far longer than any retry could run.</para>
        /// </remarks>
        public string? HtmlBody { get; set; }

        /// <summary>Where a reply should go, when that is not the site.</summary>
        public string? ReplyTo { get; set; }

        /// <summary>
        /// What kind of letter this is — <c>event-booking-confirmed</c>, <c>account-confirm</c>.
        /// </summary>
        /// <remarks>
        /// A short stable slug so a screen can group and a person can search. It is not an enum:
        /// twenty callers name their own letters and a closed list would have to be edited by
        /// everybody who ever adds one, which is how a field stops being filled in.
        /// </remarks>
        public string Kind { get; set; } = "unknown";

        /// <summary>Whatever the caller was writing about, for a screen that wants to link back.</summary>
        public Guid? OrganizationId { get; set; }

        /// <inheritdoc cref="OrganizationId"/>
        public Guid? AppUserId { get; set; }

        public DateTime CreatedUtc { get; set; }

        /// <summary>How many times sending has actually been attempted.</summary>
        public int Attempts { get; set; }

        /// <summary>
        /// The earliest a sender may try again. Set on creation to "now", and pushed out by the
        /// backoff after each failure.
        /// </summary>
        public DateTime NextAttemptUtc { get; set; }

        /// <summary>
        /// Which sender has taken this row, and when.
        /// </summary>
        /// <remarks>
        /// Claim, then send, then mark. A crash in the window between the server accepting the
        /// letter and this row being marked sends it twice, and that is the right way round to
        /// fail: a duplicate is an annoyance, a letter nobody sent is the bug this table exists to
        /// end. A claim older than <c>MailSenderJob.StaleClaim</c> is taken back, so a process that
        /// died mid-send does not strand a row for ever.
        /// </remarks>
        public DateTime? ClaimedUtc { get; set; }

        /// <inheritdoc cref="ClaimedUtc"/>
        public string? ClaimedBy { get; set; }

        /// <summary>
        /// When the mail server ACCEPTED it. Not when anybody received it.
        /// </summary>
        /// <remarks>
        /// The name is the whole point. Real delivery is only knowable from bounce reports we do
        /// not collect, and a column called <c>SentUtc</c> on a screen quietly becomes a claim that
        /// it arrived — which is exactly the claim that was wrong on 2026-08-31.
        /// </remarks>
        public DateTime? AcceptedBySmtpUtc { get; set; }

        /// <summary>When it was given up on, after the last attempt or a permanent refusal.</summary>
        public DateTime? FailedUtc { get; set; }

        /// <summary>
        /// What went wrong last time, in the server's own words where there are any.
        /// </summary>
        public string? LastError { get; set; }

        /// <inheritdoc cref="HtmlBody"/>
        public DateTime? BodyScrubbedUtc { get; set; }

        /// <summary>Still waiting for its turn: neither accepted nor given up on.</summary>
        public bool IsWaiting => AcceptedBySmtpUtc is null && FailedUtc is null;

        public virtual ICollection<OutboxEmailAttachment> Attachments { get; set; } = [];
    }

    /// <summary>One file travelling with a queued letter.</summary>
    /// <remarks>
    /// A child table rather than a JSON column, so a scrub can delete the bytes and leave the row
    /// saying what was attached. The files here are small by nature — a calendar entry is about a
    /// kilobyte and a pass image two — and anything genuinely large is refused at the door with a
    /// note on the letter rather than stored.
    /// </remarks>
    public partial class OutboxEmailAttachment
    {
        public Guid Id { get; set; }
        public Guid OutboxEmailId { get; set; }

        public string FileName { get; set; } = null!;

        /// <summary>The full type, parameters included — that is what makes a calendar file offer
        /// "add to calendar" rather than "download this file".</summary>
        public string ContentType { get; set; } = null!;

        /// <summary>The bytes, or null once scrubbed.</summary>
        public byte[]? Content { get; set; }

        /// <summary>Kept after a scrub, so the row can still say how big the letter was.</summary>
        public int ByteCount { get; set; }

        public virtual OutboxEmail OutboxEmail { get; set; } = null!;
    }
}
