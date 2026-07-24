using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

public class CaseProfile : Profile
{
    public CaseProfile()
    {
        CreateMap<Case, CaseRecord>();
    }
}

public class CaseTimelineEntryProfile : Profile
{
    public CaseTimelineEntryProfile()
    {
        CreateMap<CaseTimelineEntry, CaseTimelineEntryRecord>()
            .ForMember(d => d.AuthorDisplayName,
                       o => o.MapFrom(s => s.AuthorAppUser != null ? s.AuthorAppUser.DisplayName : null))
            .ForMember(d => d.ExperienceTypeIds,
                       o => o.MapFrom(s => s.ExperienceTypes.Select(x => x.ExperienceTypeId).ToList()));
    }
}
