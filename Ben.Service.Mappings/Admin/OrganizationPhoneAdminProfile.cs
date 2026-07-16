namespace Ben.Service.Mappings.Admin;

public class OrganizationPhoneAdminProfile : Profile
{
    public OrganizationPhoneAdminProfile()
    {
        CreateMap<OrganizationPhone, OrganizationPhoneAdminRecord>();
    }
}
