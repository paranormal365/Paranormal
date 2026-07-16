namespace Ben.Service.Mappings.Admin;

public class UserAddressAdminProfile : Profile
{
    public UserAddressAdminProfile()
    {
        CreateMap<UserAddress, UserAddressAdminRecord>();
    }
}
