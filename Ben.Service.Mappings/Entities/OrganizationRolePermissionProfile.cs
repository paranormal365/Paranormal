namespace Ben.Service.Mappings.Entities;

public class OrganizationRolePermissionProfile : Profile
{
    public OrganizationRolePermissionProfile()
    {
        CreateMap<OrganizationRolePermission, OrganizationRolePermissionRecord>();
    }
}
