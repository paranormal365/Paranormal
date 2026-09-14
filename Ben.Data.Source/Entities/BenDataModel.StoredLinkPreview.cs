namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// What a page on another site says about itself, fetched once and kept: the card under a pasted link.
    /// </summary>
    /// <remarks>
    /// <para>Beta feedback, 2026-09-14: a pasted link should show a card like one pasted on X — title, description,
    /// picture, site. Until then a link to anywhere else showed only its host, on purpose, because fetching a stranger's
    /// page from our server on anybody's request is how a server is turned against its own network.</para>
    /// <para>So the fetch is guarded (addresses vetted before connecting and on every redirect, small size and time
    /// limits), triggered only by a signed-in person posting or pasting the link, once per address, and the picture is
    /// copied small onto our own storage rather than loaded from the other site by every reader. Reading a card never
    /// fetches.</para>
    /// <para>Named Stored… because <c>LinkPreview</c> is already the record the API returns for a card.</para>
    /// </remarks>
    public class StoredLinkPreview
    {
        public Guid Id { get; set; }

        /// <summary>The address as it was normalised for lookup.</summary>
        public string Url { get; set; } = null!;

        /// <summary>SHA-256 of <see cref="Url"/>, hex — the unique key a 2000-character column cannot be.</summary>
        public string UrlHash { get; set; } = null!;

        public string Domain { get; set; } = null!;
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? SiteName { get; set; }

        /// <summary>Where the copied picture is kept, when the page had one we could copy.</summary>
        public string? ThumbnailStoragePath { get; set; }

        public string? ThumbnailContentType { get; set; }

        /// <summary>False when the page could not be read; the card then shows the host alone.</summary>
        public bool Fetched { get; set; }

        /// <summary>Why not, for the log and the SuperAdmin — never shown to a reader.</summary>
        public string? FailureReason { get; set; }

        public DateTime FetchedUtc { get; set; }

        /// <summary>After this the next person to post the link fetches it again.</summary>
        public DateTime ExpiresUtc { get; set; }

        public Guid? FetchedByAppUserId { get; set; }
    }
}
