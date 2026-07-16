namespace Ben.Service.Mappings.Entities;

public class OrganizationPageProfile : Profile
{
    public OrganizationPageProfile()
    {
        CreateMap<OrganizationPage, OrganizationPageRecord>();
    }
}
