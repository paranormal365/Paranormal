using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

namespace Ben.Service.Mappings.Entities;

public class OrganizationAddressMapConfigProfile : Profile
{
    public OrganizationAddressMapConfigProfile()
    {
        CreateMap<OrganizationAddressMapConfig, AddressMapConfigRecord>();
    }
}
