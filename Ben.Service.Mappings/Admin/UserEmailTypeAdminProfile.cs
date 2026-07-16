namespace Ben.Service.Mappings.Admin;

public class UserEmailTypeAdminProfile : Profile
{
    public UserEmailTypeAdminProfile()
    {
        CreateMap<UserEmailType, UserEmailTypeAdminRecord>();
    }
}
