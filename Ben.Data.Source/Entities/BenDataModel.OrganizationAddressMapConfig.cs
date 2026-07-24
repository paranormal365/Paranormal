using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Stores per-address map display configuration for an OrganizationAddress.
    /// One row per address; absent row means the address is not shown on any map.
    /// Follows the same 1-to-1 optional pattern as UploadFileAudioConfig.
    /// </summary>
    public partial class OrganizationAddressMapConfig : IIDStd
    {
        public Guid Id { get; set; }

        // ── Foreign key ───────────────────────────────────────────────────────
        public Guid OrganizationAddressId { get; set; }

        // ── Visibility flags ──────────────────────────────────────────────────
        /// <summary>Whether to include this address on any map display.</summary>
        public bool IsOnMap { get; set; }

        /// <summary>Show the exact-location marker pin.</summary>
        public bool ShowMarker { get; set; } = true;

        /// <summary>Show a shaded radius region around the address instead of the exact pin.</summary>
        public bool ShowRegion { get; set; }

        /// <summary>Radius (miles) of the shaded region. Only relevant when ShowRegion is true.</summary>
        public double RegionRadiusMiles { get; set; } = 1.0;

        // ── Marker appearance ──────────────────────────────────────────────────
        /// <summary>CSS color string for the map marker (e.g. "#e63535" or "red").</summary>
        public string MarkerColor { get; set; } = "#e63535";

        /// <summary>
        /// Key into the AddressMapIconRegistry dictionary.
        /// Null = use the default map-marker-target icon.
        /// </summary>
        public string? MarkerIconKey { get; set; }

        // ── Region fill appearance ─────────────────────────────────────────────
        public string RegionFillColor { get; set; } = "#3388ff";
        public double RegionFillOpacity { get; set; } = 0.2;

        // ── Region stroke appearance ───────────────────────────────────────────
        public string RegionStrokeColor { get; set; } = "#1155cc";
        public double RegionStrokeOpacity { get; set; } = 0.8;
        public double RegionStrokeWidth { get; set; } = 2.0;

        // ── Audit ─────────────────────────────────────────────────────────────
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        // ── Navigation ────────────────────────────────────────────────────────
        public virtual OrganizationAddress OrganizationAddress { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
