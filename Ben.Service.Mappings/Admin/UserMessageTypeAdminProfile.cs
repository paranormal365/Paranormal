namespace Ben.Service.Mappings.Admin;

public class UserMessageTypeAdminProfile : Profile
{
    public UserMessageTypeAdminProfile()
    {
        CreateMap<UserMessageType, UserMessageTypeAdminRecord>();
    }
}
