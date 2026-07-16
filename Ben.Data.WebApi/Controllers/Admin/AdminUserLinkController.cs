using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Admin;

[Route("api/admin/user-links")]
public sealed class AdminUserLinkController : AdminEntityControllerBase<UserLink, UserLinkAdminRecord>
{
    public AdminUserLinkController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper, IAuditLogService auditLog)
        : base(dbContextFactory, mapper, auditLog)
    {
    }
}
