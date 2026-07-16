namespace Ben.Service.Mappings.Admin;

public class UserPhoneTypeAdminProfile : Profile
{
    public UserPhoneTypeAdminProfile()
    {
        CreateMap<UserPhoneType, UserPhoneTypeAdminRecord>();
    }
}
