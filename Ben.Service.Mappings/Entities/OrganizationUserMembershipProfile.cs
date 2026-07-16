namespace Ben.Service.Mappings.Entities;

public class OrganizationUserMembershipProfile : Profile
{
    public OrganizationUserMembershipProfile()
    {
        CreateMap<OrganizationUserMembership, OrganizationUserMembershipRecord>();
    }
}