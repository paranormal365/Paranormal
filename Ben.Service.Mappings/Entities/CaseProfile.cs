using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

public class CaseProfile : Profile
{
    public CaseProfile()
    {
        CreateMap<Case, CaseRecord>()
            .ForMember(d => d.CaseManagerDisplayName,
                       o => o.MapFrom(s => s.CaseManagerAppUser != null ? s.CaseManagerAppUser.DisplayName : null));
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
                       o => o.MapFrom(s => s.ExperienceTypes.Select(x => x.ExperienceTypeId).ToList()))
            .ForMember(d => d.Files,
                       o => o.MapFrom(s => s.Files.Select(f => new CaseTimelineFileRecord
                       {
                           FileId      = f.UploadFileId,
                           FileName    = f.UploadFile.FileName,
                           ContentType = f.UploadFile.ContentType,
                           FileSize    = f.UploadFile.FileSize,
                       }).ToList()));
    }
}
