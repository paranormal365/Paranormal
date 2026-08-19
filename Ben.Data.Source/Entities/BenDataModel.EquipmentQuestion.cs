using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A question somebody asked the owner of a piece of equipment before borrowing it, and the
    /// owner's answer.
    /// </summary>
    /// <remarks>
    /// <para><b>Anonymous in both directions.</b> The asker does not learn who owns the piece, and
    /// the owner does not learn who is asking. Ben's reason is the one that matters: a question is
    /// how you find out whether a thing has a quirk you need to know about, and people do not ask
    /// that of someone whose answer might affect whether they get lent the gear.</para>
    ///
    /// <para>The identities are <b>stored</b> — <see cref="AskedByAppUserId"/> and
    /// <see cref="AnsweredByAppUserId"/> — because abuse has to be traceable and a question has to
    /// reach the right inbox. They are never projected across the divide: the record the owner
    /// receives is a separate shape that structurally has no asker field, so an anonymity rule
    /// cannot be lost to a careless edit of a shared projection.</para>
    ///
    /// <para>The anonymity is confined to this channel and the FAQ it feeds. Loans keep names on
    /// both sides — you should know who is holding your recorder — and a group's shared-gear list
    /// still names the owner.</para>
    /// </remarks>
    public class EquipmentQuestion : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid EquipmentItemId { get; set; }

        /// <summary>Stored for abuse handling and for routing the answer back. Never projected to the answerer.</summary>
        public Guid AskedByAppUserId { get; set; }

        public string QuestionText { get; set; } = string.Empty;

        public string? AnswerText { get; set; }

        public EquipmentQuestionStatus Status { get; set; } = EquipmentQuestionStatus.Open;

        /// <summary>Never projected to the asker — it is the owner, or whoever manages the group's gear.</summary>
        public Guid? AnsweredByAppUserId { get; set; }

        public DateTime? AnsweredDate { get; set; }

        /// <summary>
        /// Set when this answer was published as an FAQ entry. A stamp, not a link the reader
        /// follows: the FAQ row is a copy, so this records that it happened and stops it happening
        /// twice.
        /// </summary>
        public Guid? PromotedToFaqId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual EquipmentItem EquipmentItem { get; set; } = null!;
        public virtual AppUser AskedByAppUser { get; set; } = null!;
        public virtual AppUser? AnsweredByAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
