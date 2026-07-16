namespace Ben.Service.Mappings.Admin;

public class AppRoleClaimAdminProfile : Profile
{
    public AppRoleClaimAdminProfile()
    {
        CreateMap<IdentityRoleClaim<Guid>, AppRoleClaimAdminRecord>();
    }
}
