namespace Ben.Service.Mappings.Admin;

public class UserAddressTypeAdminProfile : Profile
{
    public UserAddressTypeAdminProfile()
    {
        CreateMap<UserAddressType, UserAddressTypeAdminRecord>();
    }
}
