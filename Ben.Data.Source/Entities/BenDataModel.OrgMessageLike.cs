namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One person's like on one feed post (item 186 F3).
    /// </summary>
    /// <remarks>
    /// <para>Composite primary key on (OrgMessageId, LikerAppUserId) — the same shape as
    /// <see cref="OrgMessageView"/>, and for the same reason: the key IS the rule. Liking twice
    /// cannot double-count, because there is nowhere to put the second row.</para>
    ///
    /// <para><b>No denormalized counter on the post.</b> Counts are computed per page beside the
    /// reply counts, which keeps one source of truth. A cached count on OrgMessage would be a
    /// second one, and the first time an unlike raced a like it would start drifting — with
    /// nothing to reconcile it against, since the rows are the only record of who actually liked.
    /// </para>
    ///
    /// <para>Kept as its own table rather than a column on a reactions table with a type: the feed
    /// has one reaction and adding a second is a product decision nobody has made. A type column
    /// added "for later" is a column every query must remember to filter, forever.</para>
    /// </remarks>
    public class OrgMessageLike
    {
        public Guid OrgMessageId { get; set; }
        public Guid LikerAppUserId { get; set; }
        public DateTime DateLiked { get; set; }

        public virtual OrgMessage OrgMessage { get; set; } = null!;
        public virtual AppUser LikerAppUser { get; set; } = null!;
    }
}
