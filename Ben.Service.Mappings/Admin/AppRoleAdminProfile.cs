namespace Ben.Service.Mappings.Admin;

public class AppRoleAdminProfile : Profile
{
    public AppRoleAdminProfile()
    {
        CreateMap<IdentityRole<Guid>, AppRoleAdminRecord>();
    }
}
