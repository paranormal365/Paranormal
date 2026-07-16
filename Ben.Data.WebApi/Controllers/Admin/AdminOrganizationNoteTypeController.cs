using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Admin;

[Route("api/admin/organization-note-types")]
public sealed class AdminOrganizationNoteTypeController : AdminEntityControllerBase<OrganizationNoteType, OrganizationNoteTypeAdminRecord>
{
    public AdminOrganizationNoteTypeController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper, IAuditLogService auditLog)
        : base(dbContextFactory, mapper, auditLog)
    {
    }
}
