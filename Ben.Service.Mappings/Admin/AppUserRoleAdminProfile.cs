namespace Ben.Service.Mappings.Admin;

public class AppUserRoleAdminProfile : Profile
{
    public AppUserRoleAdminProfile()
    {
        CreateMap<IdentityUserRole<Guid>, AppUserRoleAdminRecord>();
    }
}
