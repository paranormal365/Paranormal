namespace Ben.Service.Mappings.Entities;

public class UserLinkTypeProfile : Profile
{
    public UserLinkTypeProfile()
    {
        CreateMap<UserLinkType, UserLinkTypeRecord>();
    }
}
