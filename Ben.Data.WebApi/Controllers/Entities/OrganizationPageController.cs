using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Entities;

[Route("api/organization-pages")]
public sealed class OrganizationPageController : EntityReadControllerBase<OrganizationPage, OrganizationPageRecord>
{
    public OrganizationPageController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper)
        : base(dbContextFactory, mapper)
    {
    }
}
