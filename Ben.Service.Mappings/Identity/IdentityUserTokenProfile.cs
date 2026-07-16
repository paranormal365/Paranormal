namespace Ben.Service.Mappings.Identity;

public class IdentityUserTokenProfile : Profile
{
    public IdentityUserTokenProfile()
    {
        CreateMap<IdentityUserToken<Guid>, IdentityUserTokenRecord>()
            .ForSourceMember(src => src.Value, opt => opt.DoNotValidate());
    }
}
