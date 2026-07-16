namespace Ben.Service.Mappings.Identity;

public class IdentityRoleClaimProfile : Profile
{
    public IdentityRoleClaimProfile()
    {
        CreateMap<IdentityRoleClaim<Guid>, IdentityRoleClaimRecord>();
    }
}
