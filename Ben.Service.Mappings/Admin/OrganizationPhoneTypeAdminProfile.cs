namespace Ben.Service.Mappings.Admin;

public class OrganizationPhoneTypeAdminProfile : Profile
{
    public OrganizationPhoneTypeAdminProfile()
    {
        CreateMap<OrganizationPhoneType, OrganizationPhoneTypeAdminRecord>();
    }
}
