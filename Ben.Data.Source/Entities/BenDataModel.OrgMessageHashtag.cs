namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One <c>#tag</c> used by one post.
    /// </summary>
    /// <remarks>
    /// <para><see cref="Tag"/> is stored <b>lower-cased and without the hash</b>, and that is a
    /// storage decision with a user-visible consequence: <c>#EVP</c>, <c>#evp</c> and <c>#Evp</c>
    /// are one tag. Anything else means a tag page that shows a third of its posts, which is worse
    /// than no tag page. What the author actually typed survives in the post body, so a tag can
    /// still read the way they wrote it while gathering with its siblings.</para>
    ///
    /// <para>Normalising at write time rather than comparing case-insensitively at read time keeps
    /// the index usable: <c>WHERE Tag = @tag</c> seeks, <c>WHERE LOWER(Tag) = @tag</c> scans.</para>
    /// </remarks>
    public partial class OrgMessageHashtag
    {
        public Guid Id { get; set; }

        public Guid OrgMessageId { get; set; }

        /// <summary>The tag, lower-cased, with no leading <c>#</c>. See the remarks.</summary>
        public string Tag { get; set; } = null!;

        public DateTime DateCreated { get; set; }

        public virtual OrgMessage OrgMessage { get; set; } = null!;
    }
}
