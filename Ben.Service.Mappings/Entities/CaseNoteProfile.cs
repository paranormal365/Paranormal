using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

public class CaseNoteProfile : Profile
{
    public CaseNoteProfile()
    {
        CreateMap<CaseNote, CaseNoteRecord>()
            .ForMember(d => d.AuthorDisplayName,
                       o => o.MapFrom(s => s.AuthorAppUser != null ? s.AuthorAppUser.DisplayName : null));
    }
}
