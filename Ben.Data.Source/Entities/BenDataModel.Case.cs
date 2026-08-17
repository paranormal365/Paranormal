using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    public partial class Case : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>
        /// The readable part of this case's public address — <c>/o/{org}/cases/{UrlName}</c>.
        /// </summary>
        /// <remarks>
        /// <para>Generated from the case's <b>title</b> the first time it is published, and then
        /// left alone. The title is already public on the case page, so nothing new is exposed by
        /// putting it in the URL — which is not true of free text, and is the reason this is not a
        /// field somebody types.</para>
        ///
        /// <para><b>A case is somebody's home.</b> A slug is public text that will end up in browser
        /// histories, referrer headers and pasted links, so it must never carry a street address.
        /// <c>UrlSlug.LooksLikeAStreetAddress</c> refuses one that does — the same instinct behind
        /// redacting the coordinates.</para>
        ///
        /// <para>Null while the case is not public, because a private case has no address to
        /// promise. Once set it does not change: renaming a case must not break a link somebody has
        /// already shared.</para>
        /// </remarks>
        public string? UrlName { get; set; }

        /// <summary>
        /// The physical location this case concerns, once one has been resolved.
        /// </summary>
        /// <remarks>
        /// <para>Nullable, and the case keeps its own address columns. The address on a case is the
        /// record of what the client actually reported and must not be rewritten by a shared row
        /// that a different organization may later correct. The place is the shared identity used
        /// for mapping and for "who else has been here"; the case address is the paperwork.</para>
        ///
        /// <para>Populated for existing rows by the <c>BackfillPlacesFromCases</c> migration.</para>
        /// </remarks>
        public Guid? PlaceId { get; set; }

        public virtual Place? Place { get; set; }
    }
}
