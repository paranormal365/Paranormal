using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Entities;

// SuperAdmin-only: this thin base-class subclass has no per-row ownership/visibility
// filtering (EntityReadControllerBase.GetAll/GetById return every row unfiltered), so it
// must not be reachable by regular authenticated users. See EntityReadControllerBase's
// doc comment.
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/user-emails")]
public sealed class UserEmailController : EntityReadControllerBase<UserEmail, UserEmailRecord>
{
    public UserEmailController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper)
        : base(dbContextFactory, mapper)
    {
    }
}
