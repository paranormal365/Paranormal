using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A colour a party wears, and what it means (item 235 phase 7).
    /// </summary>
    /// <remarks>
    /// <para>Ben, 2026-09-13: <i>"blue could be the full event with food, purple could be the full
    /// event, red is day one … lets their employees know by glance what a person is registered
    /// for … or lanyard color or shirt color or color displayed on the qr code reader next to name
    /// of guest."</i></para>
    ///
    /// <para><b>The colour is the venue's and the site never invents one.</b> A venue with orange
    /// wristbands in a drawer needs the screen to say orange; offering a palette of our own would
    /// mean a steward matching a shade on a screen to a band in a box, which is exactly the
    /// mistake this is meant to prevent.</para>
    ///
    /// <para><b>It carries a NAME as well as a colour.</b> A chip that is only a colour is useless
    /// to the one steward in twelve who cannot separate red from green — the same rule the plan's
    /// squares follow.</para>
    ///
    /// <para><b>Most of them are derived</b>, from the rule: a venue that had to tag two hundred
    /// parties by hand would tag none of them. What cannot be derived is anything about food,
    /// because nothing on a booking says a party is eating; that stays a by-hand band until dining
    /// lands in phase 13.</para>
    /// </remarks>
    public partial class HostedEventBand : IAuditableEntity
    {
        public Guid Id { get; set; }

        public Guid HostedEventId { get; set; }

        /// <summary>What the venue calls the colour: "Blue", "Orange", "Glow-in-the-dark".</summary>
        public string Colour { get; set; } = string.Empty;

        /// <summary>What wearing it means: "The whole weekend, with dinner".</summary>
        public string Meaning { get; set; } = string.Empty;

        /// <summary>
        /// An optional swatch, so the chip can be drawn in something like the real colour.
        /// </summary>
        /// <remarks>
        /// Optional on purpose. A venue that never fills it in still gets a working screen — the
        /// name is what a steward reads — and a venue that does gets a chip they can match against
        /// the box of bands in their hand.
        /// </remarks>
        public string? Hex { get; set; }

        /// <summary>Who wears it, or that somebody decides by hand.</summary>
        public HostedEventBandRule Rule { get; set; } = HostedEventBandRule.ByHand;

        /// <summary>
        /// The venue's own order, which is also the order the rules are tried in.
        /// </summary>
        /// <remarks>
        /// A party staying every night matches "every night" and could also match "some nights";
        /// the first rule that matches wins, so the order is a decision the venue makes rather
        /// than one the code makes for them.
        /// </remarks>
        public int SortOrder { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEvent HostedEvent { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
