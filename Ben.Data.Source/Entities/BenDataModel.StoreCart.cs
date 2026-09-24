
namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A shopper's cart, kept on the server (storefront).
    /// </summary>
    /// <remarks>
    /// <para>Belongs to an account (<see cref="AppUserId"/>) or to a browser
    /// (<see cref="GuestTokenHash"/>, the SHA-256 of the 43-character token in the HttpOnly
    /// <c>ben.cart</c> cookie) — never neither, which a CHECK enforces. Signing in merges the guest
    /// cart into the account's. Prices are never stored here: every read re-reads the variant.</para>
    ///
    /// <para>A row is created only after a write validates, so a script sending made-up tokens
    /// cannot fill the table.</para>
    /// </remarks>
    public class StoreCart
    {
        public Guid Id { get; set; }
        public Guid? AppUserId { get; set; }
        public string? GuestTokenHash { get; set; }

        /// <summary>The code applied at the cart; re-validated on every read.</summary>
        public Guid? CouponId { get; set; }

        public DateTime LastActivityUtc { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }

        public virtual AppUser? AppUser { get; set; }
        public virtual StoreCoupon? Coupon { get; set; }
        public virtual ICollection<StoreCartItem> Items { get; set; } = [];
    }
}
