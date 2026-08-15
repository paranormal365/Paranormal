namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One sitewide configuration value, keyed by a stable string.
    /// </summary>
    /// <remarks>
    /// <para>Key/value rather than a column per setting, deliberately. Sitewide settings arrive one
    /// at a time and forever — a column each means a migration each, and a wide single-row table
    /// nobody wants to read. The cost is that values are strings and callers must parse; that cost
    /// is paid once in <c>SiteSettingKeys</c> and the typed accessors beside it, not at every call
    /// site.</para>
    ///
    /// <para>Nothing personal belongs here. This table is read by anyone the site chooses to show
    /// a setting to, and rows are edited by SuperAdmins who have no relationship to the people a
    /// personal value would describe.</para>
    /// </remarks>
    public partial class SiteSetting
    {
        /// <summary>Stable identifier — see <c>SiteSettingKeys</c>. Unique, never localised.</summary>
        public string Key { get; set; } = null!;

        /// <summary>
        /// The value as text. Null means "explicitly unset", which is not the same as the row
        /// being absent: an unset row still carries its description for the admin UI.
        /// </summary>
        public string? Value { get; set; }

        /// <summary>
        /// What this setting does, in plain language. Stored rather than hardcoded in the page so
        /// a setting added by a later migration explains itself without a UI change.
        /// </summary>
        public string? Description { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
