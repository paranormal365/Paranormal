namespace Ben.Service.Mappings.Admin;

public class OrganizationEmailTypeAdminProfile : Profile
{
    public OrganizationEmailTypeAdminProfile()
    {
        CreateMap<OrganizationEmailType, OrganizationEmailTypeAdminRecord>();
    }
}
