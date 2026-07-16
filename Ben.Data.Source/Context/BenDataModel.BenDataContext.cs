using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Ben.Data.Source.Entities;

namespace Ben.Data.Source.Context
{

    // Base class (IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>) is declared
    // in BenDataContext.Generated.cs — do not repeat it here.
    public partial class BenDataContext
    {
    }
}
