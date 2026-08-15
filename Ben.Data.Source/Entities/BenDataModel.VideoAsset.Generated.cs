using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One entry in the sitewide clipart/callout catalog served to Ben.Video.Editor by
    /// <c>GET /api/video-assets</c>. Curated by SuperAdmins, readable by everyone.
    /// </summary>
    /// <remarks>
    /// The binary lives in <see cref="UploadFile"/> like every other file in the system, so this
    /// row is metadata plus capability flags. Deliberately global: the whole point is one library
    /// every group can draw on, so there is no organization scope here.
    /// </remarks>
    public partial class VideoAsset
    {
        public string Name { get; set; } = null!;
        public string? Description { get; set; }

        /// <summary>Grouping shown in the editor's browser — "Arrows", "Shapes".</summary>
        public string? Category { get; set; }

        /// <summary>Comma-separated search tags. Split on read; a join table would be three
        /// tables of ceremony for a curated list a SuperAdmin types by hand.</summary>
        public string? Tags { get; set; }

        public VideoAssetType Type { get; set; }
        public VideoAssetFormat Format { get; set; }

        /// <summary>The asset binary.</summary>
        public Guid UploadFileId { get; set; }

        /// <summary>
        /// Small raster preview served without auth. Null falls back to the asset itself, which
        /// is fine for SVG and PNG and wasteful for anything large.
        /// </summary>
        public Guid? ThumbnailUploadFileId { get; set; }

        /// <summary>
        /// SHA-256 of the binary, hex. The editor caches by this and re-downloads when it
        /// changes, so it must be recomputed whenever the file is replaced.
        /// </summary>
        public string ContentHash { get; set; } = null!;

        public long FileSizeBytes { get; set; }
        public int? NativeWidth { get; set; }
        public int? NativeHeight { get; set; }

        // ── Capability flags: what the editor lets a user do with this asset ──
        public bool AllowRecolor { get; set; }
        public bool AllowResize { get; set; } = true;
        public bool AllowOpacity { get; set; } = true;
        public bool AllowRotation { get; set; } = true;
        public bool AllowEffects { get; set; }
        public bool AllowEasing { get; set; }
        public bool AllowMotion { get; set; } = true;
        public bool AllowControlPoints { get; set; }

        /// <summary>Comma-separated hex colours offered instead of a free picker. Null = free.</summary>
        public string? PresetColors { get; set; }

        public double? MinScale { get; set; }
        public double? MaxScale { get; set; }

        /// <summary>Whether the editor flattens this to raster on export.</summary>
        public bool FlattenOnExport { get; set; } = true;

        /// <summary>
        /// Retired assets stay in the table so existing projects can still resolve them, but
        /// drop out of the catalog. Deleting the row would break projects that reference it.
        /// </summary>
        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual UploadFile? ThumbnailUploadFile { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
