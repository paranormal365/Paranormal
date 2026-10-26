using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    public partial class UploadFileAudioConfig : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>
        /// The EVP scan settings last used on this recording, as JSON, so the next scan starts from
        /// them (phase 5, 10/26/2026). Written by the scan itself, never by a view save, which
        /// rewrites the rest of this row: <c>EditStateJson</c> is rebuilt from a fresh record on
        /// every save, so anything else kept there would be lost.
        /// </summary>
        public string? EvpScanOptionsJson { get; set; }
    }
}
