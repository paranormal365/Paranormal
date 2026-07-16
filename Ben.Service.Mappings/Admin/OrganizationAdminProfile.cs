namespace Ben.Service.Mappings.Admin;

public class OrganizationAdminProfile : Profile
{
    public OrganizationAdminProfile()
    {
        CreateMap<Organization, OrganizationAdminRecord>();
    }
}
