namespace Ben.Service.Mappings.Entities;

public class UserAddressTypeProfile : Profile
{
    public UserAddressTypeProfile()
    {
        CreateMap<UserAddressType, UserAddressTypeRecord>();
    }
}
