namespace Ben.Service.Mappings.Admin;

public class UserPhoneAdminProfile : Profile
{
    public UserPhoneAdminProfile()
    {
        CreateMap<UserPhone, UserPhoneAdminRecord>();
    }
}
