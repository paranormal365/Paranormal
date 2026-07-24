using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.RepositoryService.EntityInterfaces;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.EntityFrameworkCore;

namespace Ben.Service.RepositoryService.Repositories;

public class OrganizationAddressMemberAccessRepository : RepositoryBase<OrganizationAddressMemberAccess>, IOrganizationAddressMemberAccessRepository
{
    public OrganizationAddressMemberAccessRepository(IDbContextFactory<BenDataContext> context) : base(context) { }
}
