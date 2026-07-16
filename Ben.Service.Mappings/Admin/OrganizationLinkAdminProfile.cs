namespace Ben.Service.Mappings.Admin;

public class OrganizationLinkAdminProfile : Profile
{
    public OrganizationLinkAdminProfile()
    {
        CreateMap<OrganizationLink, OrganizationLinkAdminRecord>();
    }
}
