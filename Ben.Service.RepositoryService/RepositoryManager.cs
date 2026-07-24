using Ben.Data.Source.Context;
using Ben.Service.RepositoryService.GenericInterfaces;
using Ben.Service.RepositoryService.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Ben.Service.RepositoryService
{
    public class RepositoryManager : IRepositoryManager
    {
        private readonly IDbContextFactory<BenDataContext> dbContextFactory;
        private IOrganizationRepositoryManager? organizationRepositoryManager;
        private IAppUserRepositoryManager? appUserRepositoryManager;


        public RepositoryManager(IDbContextFactory<BenDataContext> dbContextFactory)
        {
            this.dbContextFactory = dbContextFactory;
        }


        public IOrganizationRepositoryManager Organization => organizationRepositoryManager ??= new OrganizationRepositoryManager(dbContextFactory);
        public IAppUserRepositoryManager AppUser => appUserRepositoryManager ??= new AppUserRepositoryManager(dbContextFactory);
    }
}
