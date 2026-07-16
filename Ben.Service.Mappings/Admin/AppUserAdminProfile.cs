namespace Ben.Service.Mappings.Admin;

public class AppUserAdminProfile : Profile
{
    public AppUserAdminProfile()
    {
        CreateMap<AppUser, AppUserAdminRecord>()
            .ForMember(dest => dest.IsEmailConfirmed, opt => opt.MapFrom(src => src.EmailConfirmed))
            .ForMember(dest => dest.IsPhoneNumberConfirmed, opt => opt.MapFrom(src => src.PhoneNumberConfirmed))
            .ForMember(dest => dest.IsTwoFactorEnabled, opt => opt.MapFrom(src => src.TwoFactorEnabled))
            .ForMember(dest => dest.IsLockoutEnabled, opt => opt.MapFrom(src => src.LockoutEnabled));
    }
}
