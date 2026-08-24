using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// The tax rate for one US state (item 168). Resolution is by the billed group's state; no
    /// row means no tax, which is the honest default — many states do not tax this service, and
    /// a wrong zero is visible on the bill while a wrong guess is not.
    /// </summary>
    /// <remarks>
    /// The CURRENT rate lives here and may be edited; every document freezes the rate it used
    /// onto its own ledger row, so editing a rule never rewrites history.
    /// </remarks>
    public class TaxRateRule : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>Two-letter state code, stored uppercase, unique.</summary>
        public string State { get; set; } = string.Empty;

        public decimal RatePercent { get; set; }

        /// <summary>Where the rate came from — a statute name, a date checked — for the next
        /// administrator wondering whether it is still right.</summary>
        public string? Notes { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
