namespace Ben.Service.Mappings.Admin;

public class UserLinkTypeAdminProfile : Profile
{
    public UserLinkTypeAdminProfile()
    {
        CreateMap<UserLinkType, UserLinkTypeAdminRecord>();
    }
}
