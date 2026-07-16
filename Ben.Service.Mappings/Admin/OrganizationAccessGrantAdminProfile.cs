namespace Ben.Service.Mappings.Admin;

public class OrganizationAccessGrantAdminProfile : Profile
{
    public OrganizationAccessGrantAdminProfile()
    {
        CreateMap<OrganizationAccessGrant, OrganizationAccessGrantAdminRecord>();
    }
}