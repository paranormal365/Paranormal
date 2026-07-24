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
    }
}
