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

    public partial class UserMessage : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>
        /// Withholds the author's identity from the recipient's inbox.
        /// </summary>
        /// <remarks>
        /// <para>For channels that are anonymous by design — a borrower asking about a piece of
        /// equipment, where neither side learns who the other is. The true
        /// <c>CreatedByAppUserId</c> is still stored: anonymity is a presentation rule, not an
        /// excuse to lose the audit trail, and abuse handling needs to know who wrote what.</para>
        ///
        /// <para>Honoured in <c>MyMessagesController</c>'s projection, which nulls the sender's
        /// name <i>and</i> id — leaving the id would deanonymise the message just as thoroughly as
        /// the name.</para>
        /// </remarks>
        public bool HideSenderIdentity { get; set; }
    }
}
