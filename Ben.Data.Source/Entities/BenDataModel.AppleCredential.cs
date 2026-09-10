namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A Sign in with Apple refresh token, kept so the person's Apple tokens can be revoked when
    /// they delete their account (item 229; App Review guideline 5.1.1(v)).
    /// </summary>
    /// <remarks>
    /// <para>One row per person per Apple client: the iPhone app and the website are different
    /// clients to Apple, with different secrets, and a token minted for one cannot be revoked
    /// through the other. A fresh sign-in through the same client replaces the row.</para>
    ///
    /// <para><see cref="ProtectedRefreshToken"/> is data-protected before it is stored; the
    /// plain token is worth an Apple session and appears nowhere else. Nothing reads it except
    /// the revocation, which also deletes the row.</para>
    /// </remarks>
    public partial class AppleCredential
    {
        public Guid Id { get; set; }
        public Guid AppUserId { get; set; }

        /// <summary>The audience the authorization code was minted for: the bundle id, or the Services ID.</summary>
        public string ClientId { get; set; } = null!;

        /// <summary>Apple's stable identifier for the person, as the exchange reported it.</summary>
        public string? Subject { get; set; }

        public string ProtectedRefreshToken { get; set; } = null!;

        public DateTime DateCreated { get; set; }

        /// <summary>Set when a revocation was attempted and refused, so a later sweep can try again.</summary>
        public DateTime? DateRevocationFailed { get; set; }

        public virtual AppUser AppUser { get; set; } = null!;
    }
}
