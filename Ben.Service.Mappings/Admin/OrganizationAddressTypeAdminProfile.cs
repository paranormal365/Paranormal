namespace Ben.Service.Mappings.Admin;

public class OrganizationAddressTypeAdminProfile : Profile
{
    public OrganizationAddressTypeAdminProfile()
    {
        CreateMap<OrganizationAddressType, OrganizationAddressTypeAdminRecord>();
    }
}
