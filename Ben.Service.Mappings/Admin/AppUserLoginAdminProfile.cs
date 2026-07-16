namespace Ben.Service.Mappings.Admin;

public class AppUserLoginAdminProfile : Profile
{
    public AppUserLoginAdminProfile()
    {
        CreateMap<IdentityUserLogin<Guid>, AppUserLoginAdminRecord>();
    }
}
