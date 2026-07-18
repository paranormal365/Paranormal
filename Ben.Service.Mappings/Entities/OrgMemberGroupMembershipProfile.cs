namespace Ben.Service.Mappings.Entities;

public class OrgMemberGroupMembershipProfile : Profile
{
    public OrgMemberGroupMembershipProfile()
    {
        CreateMap<OrgMemberGroupMembership, OrgMemberGroupMembershipRecord>();
    }
}
