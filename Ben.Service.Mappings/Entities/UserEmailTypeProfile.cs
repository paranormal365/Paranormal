namespace Ben.Service.Mappings.Entities;

public class UserEmailTypeProfile : Profile
{
    public UserEmailTypeProfile()
    {
        CreateMap<UserEmailType, UserEmailTypeRecord>();
    }
}
