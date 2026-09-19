using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A time-limited, revocable link that lets somebody with no account read one field session —
    /// or one recording out of it — and nothing else.
    /// </summary>
    /// <remarks>
    /// <para><b>Who this is for.</b> A client who wants to see what was recorded in their house, or
    /// a producer deciding whether a night is worth a film crew. Neither will make an account to
    /// look at one thing once, and the alternative people actually reach for is emailing the files
    /// — at which point the group has lost every control it had. A link that expires, can be
    /// pulled back, and says who opened it is strictly better than an attachment nobody can recall.
    /// </para>
    ///
    /// <para><b>The token is random, not derived.</b> The website's browser-ticket handles are
    /// hashes of what they stand for, because the same viewer asking for the same file must get a
    /// stable URL. This is the opposite case: the link is issued once, pasted into an email, and
    /// must stop working the moment its owner says so. A derived handle would come back the moment
    /// the same inputs recurred, which is precisely what revocation must prevent. So the token is
    /// 128 bits from the cryptographic RNG, stored, and looked up.</para>
    ///
    /// <para><b>Short, per item 201.</b> Twenty-two characters of base64url, not a token carrying
    /// claims. Nothing about the viewer, the session or the server can be read out of it, and it
    /// stays far under every proxy's query-string ceiling. It is a name for a row; the row is where
    /// every rule lives.</para>
    ///
    /// <para><b>Expiry is required, and that is deliberate.</b> A share with no end date is a public
    /// URL with extra steps — it will outlive the reason it was made, the person who made it, and
    /// any memory that it exists. <see cref="ExpiresUtc"/> is non-nullable so there is no way to
    /// create one.</para>
    ///
    /// <para><b>Coordinates default to withheld.</b> A session document carries a GPS fix per
    /// reading, and a coordinate is the most sensitive thing a session carries — it is somebody's
    /// street address when the session was recorded in a home. <see cref="IncludePositions"/> is
    /// false unless the person creating the link deliberately turns it on, so the failure mode of
    /// forgetting to think about it is withholding rather than disclosing.</para>
    /// </remarks>
    public class FieldSessionShareLink : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>The opaque string that appears in the URL. Unique, and the only way in.</summary>
        public string Token { get; set; } = null!;

        public Guid FieldSessionUploadId { get; set; }

        /// <summary>
        /// One recording, when the share is of a single piece of evidence rather than the night.
        /// </summary>
        /// <remarks>
        /// Null means the whole session — its readings, its marks and all of its recordings.
        /// Set means exactly one file: the document still travels, because a recording with no
        /// timeline around it is a clip with no provenance, but no other file can be fetched.
        /// </remarks>
        public Guid? FieldSessionUploadFileId { get; set; }

        /// <summary>Whose link it is. Revocation and the view log answer to this person.</summary>
        public Guid CreatedByAppUserId { get; set; }

        /// <summary>What it was made for — "for the producer at Channel 4" — so a list of five
        /// links is a list of five decisions rather than five identical rows.</summary>
        public string? Note { get; set; }

        /// <summary>When it stops working. Never null; see the remarks on the class.</summary>
        public DateTime ExpiresUtc { get; set; }

        /// <summary>When somebody pulled it back, and who. Kept rather than deleted: "was this
        /// ever shared, and when did that stop" is a question the row must still answer.</summary>
        public DateTime? RevokedUtc { get; set; }

        public Guid? RevokedByAppUserId { get; set; }

        /// <summary>Whether the readings' GPS fixes travel with the document. Default false.</summary>
        public bool IncludePositions { get; set; }

        /// <summary>Denormalised from <see cref="Views"/> so a list of links can show "opened 3
        /// times" without a join per row.</summary>
        public int ViewCount { get; set; }

        public DateTime? LastViewedUtc { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual FieldSessionUpload FieldSessionUpload { get; set; } = null!;
        public virtual FieldSessionUploadFile? FieldSessionUploadFile { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? RevokedByAppUser { get; set; }
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<FieldSessionShareLinkView> Views { get; set; }
            = new List<FieldSessionShareLinkView>();
    }

    /// <summary>
    /// One opening of a shared link.
    /// </summary>
    /// <remarks>
    /// <para><b>Why log at all.</b> The person who sent the link has to be able to answer "did they
    /// look at it?" — for a client waiting on a report, and for a group that needs to know its
    /// evidence went somewhere before it appeared elsewhere. A counter alone cannot say whether
    /// one person opened it eight times or eight people opened it once.</para>
    ///
    /// <para><b>The address is hashed, not stored.</b> These are people with no account who never
    /// agreed to anything; holding their IP addresses to satisfy a curiosity is not a trade worth
    /// making. A salted hash still separates one visitor from another, which is the only question
    /// the log is actually asked.</para>
    /// </remarks>
    public class FieldSessionShareLinkView : IIDStd
    {
        public Guid Id { get; set; }
        public Guid FieldSessionShareLinkId { get; set; }

        public DateTime ViewedUtc { get; set; }

        /// <summary>A salted digest of the caller's address. Never the address.</summary>
        public string? ViewerHash { get; set; }

        /// <summary>Truncated user agent — enough to say "an iPhone" or "a desktop browser".</summary>
        public string? UserAgent { get; set; }

        /// <summary>The recording fetched, when this view was a file rather than the session.</summary>
        public Guid? FieldSessionUploadFileId { get; set; }

        public virtual FieldSessionShareLink FieldSessionShareLink { get; set; } = null!;
    }
}
