namespace Ben.Service.Mappings.Admin;

public class OrganizationAddressAdminProfile : Profile
{
    public OrganizationAddressAdminProfile()
    {
        CreateMap<OrganizationAddress, OrganizationAddressAdminRecord>();
    }
}
