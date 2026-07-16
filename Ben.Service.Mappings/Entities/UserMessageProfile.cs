namespace Ben.Service.Mappings.Entities;

public class UserMessageProfile : Profile
{
    public UserMessageProfile()
    {
        CreateMap<UserMessage, UserMessageRecord>();
    }
}
