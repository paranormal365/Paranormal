namespace Ben.Service.Mappings.Entities;

public class UserLinkProfile : Profile
{
    public UserLinkProfile()
    {
        CreateMap<UserLink, UserLinkRecord>();
    }
}
