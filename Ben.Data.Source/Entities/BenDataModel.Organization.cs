using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json.Serialization;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{

    public partial class Organization : IAuditableEntity
    {
        public Guid Id { get; set; }

        // Map and directions feature toggles (owner/admin configures per org)
        public bool ShowAddressMap { get; set; }
        public bool ShowAddressDirections { get; set; }

        /// <summary>
        /// Strip embedded metadata — GPS above all — from AUDIO and VIDEO this group uploads
        /// (item 181). Defaults ON: a privacy protection nobody has to discover is worth more
        /// than one everybody has to switch on, and a group that wants the location kept can say
        /// so deliberately.
        /// </summary>
        /// <remarks>
        /// This is the group's CHOICE. Whether the choice is available is the plan's business —
        /// see <see cref="Ben.Data.Common.Enums.TierCapability.MediaMetadataStripping"/> — and
        /// whether it can be honoured is the host's, since it needs an ffmpeg remux. Stripping
        /// happens only when all three agree; images are stripped for everyone either way.
        /// </remarks>
        public bool StripMediaMetadata { get; set; } = true;

        /// <summary>
        /// The secret in the group's invitation link, or null when there is no live invitation.
        /// </summary>
        /// <remarks>
        /// <para><b>Why this exists at all.</b> Until 2026-09-20 there was no way for a group to
        /// gain a second person other than that person finding the group and applying — and
        /// applications can only be opened on a paid plan, so a new group's founder sat alone on a
        /// Members screen whose own guided tour told them to "invite people". Every membership on
        /// the site was created by founding a group, accepting an application, the solo plan, or a
        /// seeder. <c>OrganizationMembershipRequestController</c> had even written the comment
        /// "an application can arrive by a route that never reads that flag — an invite link" about
        /// a route that did not exist.</para>
        ///
        /// <para><b>One live link per group</b>, rather than one invitation per person. It is the
        /// thing an organiser actually does: paste a link into the group chat they already have.
        /// Issuing a new one replaces the old, which is also how you revoke — there is never a
        /// second live secret to forget about.</para>
        ///
        /// <para><b>It admits, it does not elevate.</b> Whoever opens it joins as an ordinary
        /// Member on the group's own starting role, and the plan gate is checked at the moment of
        /// joining, not at the moment of issuing, because a link may be shared today and used after
        /// the group subscribes.</para>
        /// </remarks>
        public string? JoinToken { get; set; }

        /// <summary>When the invitation link stops working. Null when there is no link.</summary>
        /// <remarks>
        /// A link that lives forever is a password that was pasted into a group chat once and is
        /// still valid two years later. A fortnight matches the event-staff invitation already on
        /// the site, so there is one answer to "how long do these last".
        /// </remarks>
        public DateTime? JoinTokenExpiresUtc { get; set; }

        /// <summary>Who issued the link, so a group can see whose invitation let somebody in.</summary>
        public Guid? JoinTokenCreatedByAppUserId { get; set; }
    }
}
