namespace Ben.Service.Mappings.Entities;

public class UserNoteProfile : Profile
{
    public UserNoteProfile()
    {
        CreateMap<UserNote, UserNoteRecord>();
    }
}
