namespace Ben.Service.Mappings.Entities;

public class UserPhoneProfile : Profile
{
    public UserPhoneProfile()
    {
        CreateMap<UserPhone, UserPhoneRecord>();
    }
}
