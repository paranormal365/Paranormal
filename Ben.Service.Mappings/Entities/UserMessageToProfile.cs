namespace Ben.Service.Mappings.Entities;

public class UserMessageToProfile : Profile
{
    public UserMessageToProfile()
    {
        CreateMap<UserMessageTo, UserMessageToRecord>();
    }
}
