namespace Ben.Service.Mappings.Admin;

public class UserMessageAdminProfile : Profile
{
    public UserMessageAdminProfile()
    {
        CreateMap<UserMessage, UserMessageAdminRecord>();
    }
}
