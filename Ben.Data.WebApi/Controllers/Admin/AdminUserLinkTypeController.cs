using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Admin;

[Route("api/admin/user-link-types")]
public sealed class AdminUserLinkTypeController : AdminEntityControllerBase<UserLinkType, UserLinkTypeAdminRecord>
{
    public AdminUserLinkTypeController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper, IAuditLogService auditLog)
        : base(dbContextFactory, mapper, auditLog)
    {
    }
}
