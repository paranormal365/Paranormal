using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.RepositoryService.EntityInterfaces;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.EntityFrameworkCore;

namespace Ben.Service.RepositoryService.Repositories;

public class OrganizationAddressRepository : RepositoryBase<OrganizationAddress>, IOrganizationAddressRepository
{
    public OrganizationAddressRepository(IDbContextFactory<BenDataContext> context) : base(context)
    {
    }
    // NOTE: geocoding (AddressGeocodingService.TryResolveCoordinates) must be applied
    // in the OrganizationAddressController Create/Update actions before calling SaveChangesAsync.
}
