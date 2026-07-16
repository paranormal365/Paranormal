namespace Ben.Service.Mappings.Entities;

public class OrganizationLinkTypeProfile : Profile
{
    public OrganizationLinkTypeProfile()
    {
        CreateMap<OrganizationLinkType, OrganizationLinkTypeRecord>();
    }
}
