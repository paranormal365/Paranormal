using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One cap on one band — "Small group may have 25 pieces of equipment".
    /// </summary>
    /// <remarks>
    /// <para><b>No row means no cap.</b> That is the deliberate default, and it is the safe
    /// direction: a limit that appears because somebody forgot to write a row would lock groups out
    /// of features they are paying for, and they would report it as the platform being broken. A
    /// missing cap costs the platform a little money and nobody's afternoon.</para>
    ///
    /// <para><b><see cref="MaxValue"/> is nullable and null means unlimited</b>, which is different
    /// from having no row at all only in that it is written down. Worth being able to say
    /// explicitly: "the top band has no equipment limit" is a decision, and a screen that cannot
    /// show it looks like an oversight.</para>
    ///
    /// <para>Zero is a real value and means the feature is off for that band — the shape of a
    /// paid-only feature, as opposed to a metered one.</para>
    /// </remarks>
    public class SubscriptionTierLimit : IAuditableEntity
    {
        public Guid Id { get; set; }

        public Guid SubscriptionTierId { get; set; }

        /// <summary>What is being capped.</summary>
        public SubscriptionLimit Limit { get; set; }

        /// <summary>The cap. Null is unlimited; zero turns the feature off for this band.</summary>
        public int? MaxValue { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual SubscriptionTier SubscriptionTier { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
