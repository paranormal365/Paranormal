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

    public partial class UserEmail : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>
        /// When a validation link was last issued. Null until the first send. Used to throttle
        /// resend requests and to expire a token that was never redeemed — nothing else on this
        /// row records when a token was issued, only the token itself.
        /// </summary>
        public DateTime? DateValidationSent { get; set; }
    }
}
