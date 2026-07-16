namespace Ben.Service.Mappings.Identity;

public class IdentityRoleProfile : Profile
{
    public IdentityRoleProfile()
    {
        CreateMap<IdentityRole<Guid>, IdentityRoleRecord>();
    }
}
