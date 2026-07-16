namespace Ben.Service.Mappings.Entities;

public class OrganizationNoteProfile : Profile
{
    public OrganizationNoteProfile()
    {
        CreateMap<OrganizationNote, OrganizationNoteRecord>();
    }
}
