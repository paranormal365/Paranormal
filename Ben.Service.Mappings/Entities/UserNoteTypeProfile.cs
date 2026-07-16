namespace Ben.Service.Mappings.Entities;

public class UserNoteTypeProfile : Profile
{
    public UserNoteTypeProfile()
    {
        CreateMap<UserNoteType, UserNoteTypeRecord>();
    }
}
