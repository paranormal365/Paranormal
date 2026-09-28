using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A phone that may be sent a push for the person signed in on it (item 252).
    /// </summary>
    /// <remarks>
    /// <para><b>One row per device token, owned by whoever registered it last.</b> A phone handed
    /// from one person to another keeps its token; the second sign-in takes the row over, so the
    /// first person's pushes stop reaching a phone they no longer hold. Signing out deletes it.</para>
    ///
    /// <para><b>"Signed in on the app"</b>, which is who a lead's launch reaches, means exactly
    /// this: a row here. Apple answers a dead token with 410, and the sender deletes the row then,
    /// so the table does not fill with phones that were wiped or had the app removed.</para>
    ///
    /// <para>The token is an address, not a secret — it can only be used with our signing key —
    /// but it is never logged all the same: it identifies a phone.</para>
    /// </remarks>
    public partial class PushDevice
    {
        public Guid Id { get; set; }

        public Guid AppUserId { get; set; }

        /// <summary>Apple's device token, as hex.</summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>The push service the token belongs to.</summary>
        public PushEnvironment Environment { get; set; }

        /// <summary>The app version that registered it, for knowing which builds a push reaches.</summary>
        public string? AppVersion { get; set; }

        public DateTime DateCreated { get; set; }

        /// <summary>When the app last registered it — every sign-in and every launch while signed in.</summary>
        public DateTime LastSeenUtc { get; set; }

        public virtual AppUser AppUser { get; set; } = null!;
    }
}
