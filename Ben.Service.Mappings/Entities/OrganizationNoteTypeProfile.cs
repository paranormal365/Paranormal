namespace Ben.Service.Mappings.Entities;

public class OrganizationNoteTypeProfile : Profile
{
    public OrganizationNoteTypeProfile()
    {
        CreateMap<OrganizationNoteType, OrganizationNoteTypeRecord>();
    }
}
