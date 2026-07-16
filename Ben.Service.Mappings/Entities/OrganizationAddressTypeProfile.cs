namespace Ben.Service.Mappings.Entities;

public class OrganizationAddressTypeProfile : Profile
{
    public OrganizationAddressTypeProfile()
    {
        CreateMap<OrganizationAddressType, OrganizationAddressTypeRecord>();
    }
}
