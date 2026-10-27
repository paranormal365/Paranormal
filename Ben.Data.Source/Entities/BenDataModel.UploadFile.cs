using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    public partial class UploadFile : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>
        /// The field session this recording was copied out of, or clipped from a copy of (phase 9,
        /// 10/27/2026). No foreign key: the session may be deleted and the file is still somebody's.
        /// </summary>
        public Guid? SourceFieldSessionUploadId { get; set; }

        /// <summary>Where in that session it came from, e.g. <c>media/audio-002.m4a</c>.</summary>
        public string? SourceClipPath { get; set; }
    }
}
