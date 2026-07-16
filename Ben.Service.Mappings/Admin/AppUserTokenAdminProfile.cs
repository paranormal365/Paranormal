namespace Ben.Service.Mappings.Admin;

public class AppUserTokenAdminProfile : Profile
{
    public AppUserTokenAdminProfile()
    {
        CreateMap<IdentityUserToken<Guid>, AppUserTokenAdminRecord>()
            .ForSourceMember(src => src.Value, opt => opt.DoNotValidate());
    }
}
