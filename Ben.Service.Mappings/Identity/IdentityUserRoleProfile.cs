namespace Ben.Service.Mappings.Identity;

public class IdentityUserRoleProfile : Profile
{
    public IdentityUserRoleProfile()
    {
        CreateMap<IdentityUserRole<Guid>, IdentityUserRoleRecord>();
    }
}
