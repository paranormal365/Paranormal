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
    }
}
