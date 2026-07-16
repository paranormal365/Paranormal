namespace Ben.Service.Mappings.Entities;

public class UserPhoneTypeProfile : Profile
{
    public UserPhoneTypeProfile()
    {
        CreateMap<UserPhoneType, UserPhoneTypeRecord>();
    }
}
