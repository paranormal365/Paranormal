namespace Ben.Service.Mappings.Admin;

public class UserLinkAdminProfile : Profile
{
    public UserLinkAdminProfile()
    {
        CreateMap<UserLink, UserLinkAdminRecord>();
    }
}
