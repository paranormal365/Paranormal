namespace Ben.Service.Mappings.Entities;

public class OrganizationAddressProfile : Profile
{
    public OrganizationAddressProfile()
    {
        CreateMap<OrganizationAddress, OrganizationAddressRecord>();
    }
}
