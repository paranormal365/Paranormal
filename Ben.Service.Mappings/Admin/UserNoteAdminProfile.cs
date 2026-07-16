namespace Ben.Service.Mappings.Admin;

public class UserNoteAdminProfile : Profile
{
    public UserNoteAdminProfile()
    {
        CreateMap<UserNote, UserNoteAdminRecord>();
    }
}
