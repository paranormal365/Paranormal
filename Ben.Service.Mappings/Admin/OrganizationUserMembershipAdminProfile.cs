namespace Ben.Service.Mappings.Admin;

public class OrganizationUserMembershipAdminProfile : Profile
{
    public OrganizationUserMembershipAdminProfile()
    {
        CreateMap<OrganizationUserMembership, OrganizationUserMembershipAdminRecord>();
    }
}