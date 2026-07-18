namespace Ben.Service.Mappings.Entities;

public class OrganizationLogoProfile : Profile
{
    public OrganizationLogoProfile()
    {
        CreateMap<OrganizationLogo, OrganizationLogoRecord>();
    }
}
