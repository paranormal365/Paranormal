namespace Ben.Service.Mappings.Admin;

public class UserEmailAdminProfile : Profile
{
    public UserEmailAdminProfile()
    {
        CreateMap<UserEmail, UserEmailAdminRecord>();
    }
}
