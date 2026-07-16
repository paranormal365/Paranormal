namespace Ben.Service.Mappings.Admin;

public class UserMessageToAdminProfile : Profile
{
    public UserMessageToAdminProfile()
    {
        CreateMap<UserMessageTo, UserMessageToAdminRecord>();
    }
}
