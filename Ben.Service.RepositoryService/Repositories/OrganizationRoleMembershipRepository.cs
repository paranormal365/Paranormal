using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.RepositoryService.EntityInterfaces;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.EntityFrameworkCore;

namespace Ben.Service.RepositoryService.Repositories;

public class OrganizationRoleMembershipRepository : RepositoryBase<OrganizationRoleMembership>, IOrganizationRoleMembershipRepository
{
    public OrganizationRoleMembershipRepository(IDbContextFactory<BenDataContext> context) : base(context)
    {
    }
}
