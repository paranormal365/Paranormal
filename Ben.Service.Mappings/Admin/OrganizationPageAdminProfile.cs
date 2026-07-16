namespace Ben.Service.Mappings.Admin;

public class OrganizationPageAdminProfile : Profile
{
    public OrganizationPageAdminProfile()
    {
        CreateMap<OrganizationPage, OrganizationPageAdminRecord>();
    }
}
