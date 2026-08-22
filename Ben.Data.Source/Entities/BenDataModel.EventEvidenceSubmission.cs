using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Evidence a public-event attendee offered to the group — held until a member accepts it.
    /// </summary>
    /// <remarks>
    /// <para>Item 111, Ben's decision: attendees may <b>submit</b>, a member must <b>accept</b>.
    /// Thirty strangers with phones is the whole value of a public event, and also exactly why
    /// nothing goes into the record unreviewed: the submitter may have an account created by
    /// clicking a link in an email an hour ago. The queue is the same shape as the
    /// file-permission requests — one side offers, the other side decides.</para>
    ///
    /// <para><b>An accepted submission IS the record.</b> It is not converted into a timeline
    /// entry or an investigation file on acceptance: the timeline is case-scoped and a public
    /// event's investigation often has no case, so any conversion target loses some events. The
    /// accepted rows are the visitor-evidence collection, read by the public event page and the
    /// group alike.</para>
    ///
    /// <para><b>Publicity follows item 87's recorded bargain</b> — evidence at an open
    /// investigation is public and cannot be made private — and the submitter is told so before
    /// they submit. The open sub-questions (other attendees in someone's footage, documentation
    /// vs evidence) are recorded on item 111 and deliberately not decided here.</para>
    /// </remarks>
    public class EventEvidenceSubmission : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>The public event it was captured at — the thing attendance is proven against.</summary>
        public Guid OrgCalendarEventId { get; set; }

        public Guid SubmittedByAppUserId { get; set; }

        /// <summary>The uploaded file. The bytes are the submission; this row is its review state.</summary>
        public Guid UploadFileId { get; set; }

        /// <summary>The submitter's own account of what this is — where, when, what they heard.</summary>
        public string? Note { get; set; }

        public EvidenceSubmissionStatus Status { get; set; } = EvidenceSubmissionStatus.Pending;

        public Guid? ReviewedByAppUserId { get; set; }
        public DateTime? DateReviewed { get; set; }

        /// <summary>Why it was declined, when it was. Shown to the submitter — a bare no helps nobody.</summary>
        public string? RejectionReason { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual OrgCalendarEvent OrgCalendarEvent { get; set; } = null!;
        public virtual AppUser SubmittedByAppUser { get; set; } = null!;
        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual AppUser? ReviewedByAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
