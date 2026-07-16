namespace Ben.Service.Mappings.Identity;

public class IdentityUserLoginProfile : Profile
{
    public IdentityUserLoginProfile()
    {
        CreateMap<IdentityUserLogin<Guid>, IdentityUserLoginRecord>();
    }
}
