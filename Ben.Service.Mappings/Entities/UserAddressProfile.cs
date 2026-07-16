namespace Ben.Service.Mappings.Entities;

public class UserAddressProfile : Profile
{
    public UserAddressProfile()
    {
        CreateMap<UserAddress, UserAddressRecord>();
    }
}
