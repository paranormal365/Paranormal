using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A file that goes with a product — a manual, firmware, software, a document (store sellers,
    /// backlog 251, P11). Either an uploaded file or a manual written on the site as HTML.
    /// </summary>
    /// <remarks>
    /// <para>Ben, 09/24/2026: the seller chooses, per file, whether buyers get it (downloadable from a
    /// paid order) or it stays private to the seller and the store.</para>
    ///
    /// <para><b>Never public.</b> The bytes are an ownerless, non-public upload; every download goes
    /// through a door that checks who is asking: the store's staff, the product's seller, or a buyer
    /// of it whose order is paid, not cancelled, and not wholly refunded for that product.</para>
    /// </remarks>
    public class StoreProductFile
    {
        public const int MaxTitleLength = 200;
        public const int MaxVersionLength = 40;

        /// <summary>The largest file: under the 100 MB a request can carry through the proxy.</summary>
        public const long MaxBytes = 95L * 1024 * 1024;

        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public StoreProductFileKind Kind { get; set; }
        public StoreFileAudience Audience { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? VersionLabel { get; set; }

        /// <summary>The uploaded bytes; null for a manual written on the site.</summary>
        public Guid? UploadFileId { get; set; }

        /// <summary>A manual written on the site (or imported from Markdown), sanitised HTML; null for an upload.</summary>
        public string? ManualHtml { get; set; }

        public string? FileName { get; set; }
        public long? SizeBytes { get; set; }
        public int SortOrder { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }

        public virtual StoreProduct Product { get; set; } = null!;
        public virtual UploadFile? UploadFile { get; set; }
    }
}
