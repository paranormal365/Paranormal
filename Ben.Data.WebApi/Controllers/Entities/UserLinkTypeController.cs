using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Entities;

[Route("api/user-link-types")]
public sealed class UserLinkTypeController : EntityReadControllerBase<UserLinkType, UserLinkTypeRecord>
{
    public UserLinkTypeController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper)
        : base(dbContextFactory, mapper)
    {
    }
}
