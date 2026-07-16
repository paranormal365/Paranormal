namespace Ben.Service.Mappings.Entities;

public class OrganizationProfile : Profile
{
    public OrganizationProfile()
    {
        CreateMap<Organization, OrganizationRecord>();
    }
}
