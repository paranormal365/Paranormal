using System;

namespace Ben.Data.Source.Entities
{
    public partial class UploadFileVote
    {
        public Guid UploadFileId { get; set; }
        public Guid AppUserId { get; set; }

        /// <summary>
        /// User's score for the file.
        /// Typical values: 1 = upvote, -1 = downvote.
        /// Integer so future star ratings (1–5) or other schemes are supported.
        /// </summary>
        public int Score { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }

        // ── Navigation ────────────────────────────────────────────────────────
        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual AppUser    AppUser    { get; set; } = null!;
    }
}
