namespace Ben.Service.Mappings.Entities;

public class OrganizationRoleMembershipProfile : Profile
{
    public OrganizationRoleMembershipProfile()
    {
        CreateMap<OrganizationRoleMembership, OrganizationRoleMembershipRecord>();
    }
}
