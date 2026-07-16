namespace Ben.Service.Mappings.Entities;

public class OrganizationAccessGrantProfile : Profile
{
    public OrganizationAccessGrantProfile()
    {
        CreateMap<OrganizationAccessGrant, OrganizationAccessGrantRecord>();
    }
}