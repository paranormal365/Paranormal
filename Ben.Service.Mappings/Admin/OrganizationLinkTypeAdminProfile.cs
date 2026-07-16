namespace Ben.Service.Mappings.Admin;

public class OrganizationLinkTypeAdminProfile : Profile
{
    public OrganizationLinkTypeAdminProfile()
    {
        CreateMap<OrganizationLinkType, OrganizationLinkTypeAdminRecord>();
    }
}
