namespace Ben.Service.Mappings.Entities;

public class UserEmailProfile : Profile
{
    public UserEmailProfile()
    {
        CreateMap<UserEmail, UserEmailRecord>();
    }
}
