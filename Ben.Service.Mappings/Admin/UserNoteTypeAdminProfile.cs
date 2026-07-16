namespace Ben.Service.Mappings.Admin;

public class UserNoteTypeAdminProfile : Profile
{
    public UserNoteTypeAdminProfile()
    {
        CreateMap<UserNoteType, UserNoteTypeAdminRecord>();
    }
}
