using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// What one band costs at one billing cadence — the 4–10 band, billed yearly, for $150.
    /// </summary>
    /// <remarks>
    /// <para><b>A row per interval rather than a column per interval.</b> The first shape of this
    /// was <c>MonthlyPrice</c> on the tier, and adding yearly to it means a second column, then a
    /// third for quarterly, and pricing code that switches over field names. A row keyed by
    /// <see cref="Interval"/> means a new cadence is a new row a SuperAdmin can type, and the
    /// pricing code never changes.</para>
    ///
    /// <para><b>The yearly discount is the price, not a separate percentage.</b> Storing both "$15
    /// a month" and "20% off yearly" gives two places for the yearly figure to live and one of them
    /// will drift. The editor offers "make yearly N% off twelve months" as a way to <i>fill in</i>
    /// this row, and <c>SubscriptionPricing.SavingPercentAgainstMonthly</c> reads the saving back
    /// out for display. One number, stored once.</para>
    ///
    /// <para><b>A missing row means the band is not sold at that cadence</b>, which is a real thing
    /// to want — a free band billed yearly is meaningless, and an introductory band might be
    /// monthly only. It is not an error, and the checkout simply does not offer it.</para>
    /// </remarks>
    public class SubscriptionTierPrice : IAuditableEntity
    {
        public Guid Id { get; set; }

        public Guid SubscriptionTierId { get; set; }

        /// <summary>How long the period this price buys lasts.</summary>
        public BillingInterval Interval { get; set; }

        /// <summary>
        /// The whole price for one period at this cadence — not a monthly-equivalent.
        /// </summary>
        /// <remarks>
        /// A yearly row holds the yearly figure. Storing a monthly-equivalent and multiplying is
        /// how a $149.99 yearly price becomes $149.88, and the difference shows up on a receipt.
        /// </remarks>
        public decimal Price { get; set; }

        /// <summary>
        /// What one member BEYOND the band's cap pays per period, themselves (item 144, the
        /// overflow-seat model). Null means the band cannot be outgrown — today's behavior. A
        /// top band with this set is allowed to be bounded: growth past it is priced per seat
        /// rather than by a bigger band.
        /// </summary>
        public decimal? PricePerExtraMember { get; set; }

        /// <summary>Retired prices stay for the periods that were billed against them.</summary>
        public bool IsActive { get; set; } = true;

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual SubscriptionTier SubscriptionTier { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
