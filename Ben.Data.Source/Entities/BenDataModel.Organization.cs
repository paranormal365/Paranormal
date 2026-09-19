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
    }
}
