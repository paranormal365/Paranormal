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

    public partial class OrganizationAddress : IAuditableEntity
    {
        public Guid Id { get; set; }

        // Internal geocoding reference data; not intended for public API/UI contracts.
        public string? GeocodingResponseJson { get; set; }
        public string? GeocodingResultType { get; set; }
    }
}
