using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.RepositoryService.EntityInterfaces;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.EntityFrameworkCore;

namespace Ben.Service.RepositoryService.Repositories;

public class UserPhoneTypeRepository : RepositoryBase<UserPhoneType>, IUserPhoneTypeRepository
{
    public UserPhoneTypeRepository(IDbContextFactory<BenDataContext> context) : base(context)
    {
    }
}
