using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json.Serialization;
using Ben.Data.Common.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace Ben.Data.Source.Entities
{
    // Id (Guid) is provided by IdentityUser<Guid> and satisfies IIDStd.
    public partial class AppUser : IdentityUser<Guid>, IIDStd
    {
        /// <summary>
        /// When a confirmation link was last successfully HANDED TO THE MAIL SERVER. Null means no
        /// message has ever gone out for this account.
        /// </summary>
        /// <remarks>
        /// <para>Stamped only when the send actually succeeds, which is the entire point.
        /// <c>EmailConfirmed</c> already says whether somebody clicked the link; nothing said
        /// whether a link was ever sent, so an unconfirmed account was indistinguishable from an
        /// account whose mail silently failed. Ben signed up on 2026-08-31, received nothing, and
        /// there was no record anywhere to tell him which of those had happened.</para>
        ///
        /// <para>"Handed to the mail server" is the honest limit of what this can claim. SMTP
        /// accepting a message is not delivery — it can still bounce, or land in spam — but it
        /// cleanly separates "we never tried / we failed" from "it left here", and that is the
        /// distinction that was missing.</para>
        /// </remarks>
        public DateTime? DateConfirmationSent { get; set; }

        /// <summary>When the address was confirmed. Null while <c>EmailConfirmed</c> is false.</summary>
        /// <remarks>
        /// Identity records confirmation as a bare bool with no time attached, so "confirmed" could
        /// never be placed against "sent" — which is what makes a stuck sign-up readable.
        /// </remarks>
        public DateTime? DateEmailConfirmed { get; set; }
    }
}
