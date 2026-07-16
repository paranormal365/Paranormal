using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Entities;

[Route("api/organization-phone-types")]
public sealed class OrganizationPhoneTypeController : EntityReadControllerBase<OrganizationPhoneType, OrganizationPhoneTypeRecord>
{
    public OrganizationPhoneTypeController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper)
        : base(dbContextFactory, mapper)
    {
    }
}
