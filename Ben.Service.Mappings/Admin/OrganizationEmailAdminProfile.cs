namespace Ben.Service.Mappings.Admin;

public class OrganizationEmailAdminProfile : Profile
{
    public OrganizationEmailAdminProfile()
    {
        CreateMap<OrganizationEmail, OrganizationEmailAdminRecord>();
    }
}
