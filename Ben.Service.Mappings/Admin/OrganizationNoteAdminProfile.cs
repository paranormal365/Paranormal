namespace Ben.Service.Mappings.Admin;

public class OrganizationNoteAdminProfile : Profile
{
    public OrganizationNoteAdminProfile()
    {
        CreateMap<OrganizationNote, OrganizationNoteAdminRecord>();
    }
}
