namespace Ben.Service.Mappings.Entities;

public class OrganizationEmailTypeProfile : Profile
{
    public OrganizationEmailTypeProfile()
    {
        CreateMap<OrganizationEmailType, OrganizationEmailTypeRecord>();
    }
}
