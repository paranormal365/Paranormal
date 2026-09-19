using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// An investigation request submitted from the signed-out wizard by somebody whose email
    /// already has an account, parked until that account's owner claims it.
    /// </summary>
    /// <remarks>
    /// <para><b>Why it is not a <see cref="ClientRequest"/>.</b> The submit endpoint is anonymous.
    /// Writing a request straight onto the account that owns the address would let any visitor
    /// fill a stranger's <i>My Requests</i> with whatever they typed; refusing with "that email
    /// has an account" would make the request form an oracle for which addresses are registered
    /// (the rule <c>AccountRegistrationController</c> keeps). So the request is held here, owned
    /// by nobody, and the only person told about it is the account holder — by email, with a
    /// link that carries <see cref="Secret"/>. Signed in as that address and holding the secret,
    /// they adopt it into a real <see cref="ClientRequest"/> or discard it.</para>
    ///
    /// <para><see cref="Secret"/> is stored as a SHA-256 hash: the link is the credential, and a
    /// read of this table must not be enough to forge one. Rows are deleted on adoption or
    /// discard and ignored after <see cref="DateExpires"/>.</para>
    /// </remarks>
    public class PendingClientRequest
    {
        public Guid Id { get; set; }

        /// <summary>The address the stranger typed, normalised upper-case like Identity's own.</summary>
        public string NormalizedEmail { get; set; } = null!;

        /// <summary>SHA-256 (base64url) of the secret in the emailed link.</summary>
        public string SecretHash { get; set; } = null!;

        /// <summary>The name given on the form — for the adopt page, so the holder recognises it.</summary>
        public string DisplayName { get; set; } = null!;

        // ── Everything ClientRequest carries, so adoption is a copy ───────────
        public string StreetAddress1 { get; set; } = null!;
        public string? StreetAddress2 { get; set; }
        public string City { get; set; } = null!;
        public string State { get; set; } = null!;
        public string ZipCode { get; set; } = null!;
        public string Country { get; set; } = "US";
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public ClientGender Gender { get; set; } = ClientGender.NotProvided;
        public int? BirthYear { get; set; }
        public string? Description { get; set; }

        /// <summary>The chosen organisations, as a JSON array of ids (at most two).</summary>
        public string OrganizationIdsJson { get; set; } = "[]";

        public DateTime DateCreated { get; set; }
        public DateTime DateExpires { get; set; }
    }
}
