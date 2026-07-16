using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Entities;

[Route("api/organization-link-types")]
public sealed class OrganizationLinkTypeController : EntityReadControllerBase<OrganizationLinkType, OrganizationLinkTypeRecord>
{
    public OrganizationLinkTypeController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper)
        : base(dbContextFactory, mapper)
    {
    }
}
