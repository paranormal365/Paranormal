using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One redeemable string. A shared coupon has exactly one of these; a generated batch has many.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the code is a row and not a column on <see cref="Coupon"/>.</b> "Single-use" and
    /// "single-use per generated code" are the same rule applied at two different scopes, and the
    /// only way to express both without a special case is to let the limit live next to the string
    /// it limits. LAUNCH25 is a coupon with one code capped at a hundred redemptions; a conference
    /// batch is the same coupon with two hundred codes capped at one each. Nothing in the pricing
    /// or redemption code has to know which it is looking at.</para>
    ///
    /// <para><b>It also makes redemption a single lookup.</b> Whatever somebody types is matched
    /// against this table, upper-cased, and the coupon comes back with it. A design that kept
    /// shared codes on the parent would need two queries and would have to decide which wins when
    /// both match.</para>
    ///
    /// <para><b>The count here is a cache, like its parent's.</b> The authority is the unique index
    /// on <see cref="CouponRedemption"/>, because two people racing for the last use of a code is a
    /// real event and not a rare one — that is exactly what the last use of a code invites.</para>
    /// </remarks>
    public class CouponCode : IAuditableEntity
    {
        public Guid Id { get; set; }

        public Guid CouponId { get; set; }

        /// <summary>
        /// The code as typed, stored upper-cased and unique across every coupon.
        /// </summary>
        /// <remarks>
        /// Case-insensitive on purpose: a code is read off an email or a badge and typed by hand,
        /// and treating <c>launch25</c> and <c>LAUNCH25</c> as different codes creates a support
        /// ticket, not a security boundary.
        /// </remarks>
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// How many times this particular code may be redeemed. Null for unlimited.
        /// </summary>
        /// <remarks>
        /// One is the single-use case, and it is the default for a generated batch. The parent's
        /// <see cref="Coupon.MaxRedemptions"/> caps the campaign as a whole on top of this, so a
        /// batch of five hundred single-use codes can still be stopped at the first fifty.
        /// </remarks>
        public int? MaxRedemptions { get; set; }

        /// <summary>Redemptions of this code so far.</summary>
        public int RedemptionCount { get; set; }

        /// <summary>
        /// Who this code was generated for, as a note. Free text, and never enforced.
        /// </summary>
        /// <remarks>
        /// For the common case where the recipient has no account here yet — a name and an email
        /// off a conference list. To actually restrict a code to somebody, set
        /// <see cref="RestrictedToAppUserId"/>; this field is what you write when you cannot.
        /// </remarks>
        public string? IssuedTo { get; set; }

        /// <summary>
        /// The only person who may redeem this code, or null for anybody holding it.
        /// </summary>
        /// <remarks>
        /// <para><b>The person, not the organization.</b> A code given to somebody as an apology or
        /// an inducement follows them: they may run one group this year and a different one next
        /// year, and tying the code to whichever group they happened to be in when it was issued
        /// makes it worthless the moment they move. The redeemer must be signed in as this account
        /// and be allowed to bill the group they are redeeming for — the second half is checked
        /// where every other billing action is, not here.</para>
        ///
        /// <para><b>On the code and not the coupon</b> so one generated batch can be individually
        /// addressed — five hundred codes, five hundred people, one set of terms. A campaign aimed
        /// at exactly one person is a batch of one, which needs no separate concept.</para>
        /// </remarks>
        public Guid? RestrictedToAppUserId { get; set; }

        /// <summary>A single code can be withdrawn without retiring the whole batch.</summary>
        public bool IsActive { get; set; } = true;

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Coupon Coupon { get; set; } = null!;
        public virtual AppUser? RestrictedToAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<CouponRedemption> Redemptions { get; set; } = [];
    }
}
