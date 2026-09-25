namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// What one pasted link said about itself the last time the server looked: title, description,
    /// picture and site name, or that it could not be read.
    /// </summary>
    /// <remarks>
    /// <para><b>One outbound fetch a week per link.</b> A board with forty link cards is opened many
    /// times by many people; without this every open would send forty requests to forty strangers'
    /// servers from ours. A successful answer is kept seven days. A failure is kept one day, so a
    /// page that was down is retried at most daily rather than on every open.</para>
    ///
    /// <para><b>Keyed by a hash of the whole URL, stored without its query string.</b>
    /// <see cref="UrlHash"/> is SHA-256 of the full normalised URL, because two links differing only
    /// in their query can be different pages. <see cref="Url"/> is for a person reading the table,
    /// and has the query removed: a pasted link can carry a signature or a one-time code
    /// (<c>?sig=</c>, <c>?code=</c>), and a cache is not a place to keep one.</para>
    ///
    /// <para>Not auditable and not tied to anybody: it records what a public page said, not what a
    /// person did, and it expires on its own.</para>
    /// </remarks>
    public class LinkUnfurlCache
    {
        public Guid Id { get; set; }

        /// <summary>Lower-case hex SHA-256 of the normalised URL, query included. Unique.</summary>
        public string UrlHash { get; set; } = null!;

        /// <summary>The normalised URL with its query string and fragment removed (2048).</summary>
        public string Url { get; set; } = null!;

        /// <summary>The HTTP status the page answered with, or 0 when it could not be reached or was refused.</summary>
        public int StatusCode { get; set; }

        public string? Title { get; set; }
        public string? Description { get; set; }

        /// <summary>The page's own https <c>og:image</c>. Never a proxy address.</summary>
        public string? ImageSourceUrl { get; set; }

        public string? SiteName { get; set; }

        public DateTime FetchedAtUtc { get; set; }

        /// <summary>After this the next request fetches the page again.</summary>
        public DateTime ExpiresAtUtc { get; set; }
    }
}
