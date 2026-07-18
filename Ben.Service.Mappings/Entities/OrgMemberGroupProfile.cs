namespace Ben.Service.Mappings.Entities;

public class OrgMemberGroupProfile : Profile
{
    public OrgMemberGroupProfile()
    {
        CreateMap<OrgMemberGroup, OrgMemberGroupRecord>();
    }
}
