namespace Ben.Service.Mappings.Admin;

public class OrganizationNoteTypeAdminProfile : Profile
{
    public OrganizationNoteTypeAdminProfile()
    {
        CreateMap<OrganizationNoteType, OrganizationNoteTypeAdminRecord>();
    }
}
