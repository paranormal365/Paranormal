namespace Ben.Service.Mappings.Entities;

public class OrganizationEmailProfile : Profile
{
    public OrganizationEmailProfile()
    {
        CreateMap<OrganizationEmail, OrganizationEmailRecord>();
    }
}
