namespace Ben.Service.Mappings.Identity;

public class IdentityUserClaimProfile : Profile
{
    public IdentityUserClaimProfile()
    {
        CreateMap<IdentityUserClaim<Guid>, IdentityUserClaimRecord>();
    }
}
