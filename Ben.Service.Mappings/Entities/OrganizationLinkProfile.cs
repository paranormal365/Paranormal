namespace Ben.Service.Mappings.Entities;

public class OrganizationLinkProfile : Profile
{
    public OrganizationLinkProfile()
    {
        CreateMap<OrganizationLink, OrganizationLinkRecord>();
    }
}
