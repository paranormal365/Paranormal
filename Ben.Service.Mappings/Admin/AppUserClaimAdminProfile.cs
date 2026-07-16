namespace Ben.Service.Mappings.Admin;

public class AppUserClaimAdminProfile : Profile
{
    public AppUserClaimAdminProfile()
    {
        CreateMap<IdentityUserClaim<Guid>, AppUserClaimAdminRecord>();
    }
}
