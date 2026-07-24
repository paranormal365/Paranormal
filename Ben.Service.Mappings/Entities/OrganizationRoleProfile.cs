namespace Ben.Service.Mappings.Entities;

public class OrganizationRoleProfile : Profile
{
    public OrganizationRoleProfile()
    {
        CreateMap<OrganizationRole, OrganizationRoleRecord>();
    }
}
