namespace Ben.Service.Mappings.Entities;

public class UserMessageTypeProfile : Profile
{
    public UserMessageTypeProfile()
    {
        CreateMap<UserMessageType, UserMessageTypeRecord>();
    }
}
