using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

namespace Ben.Service.Mappings.Entities;

public class OrganizationFileProfile : Profile
{
    public OrganizationFileProfile()
    {
        CreateMap<OrganizationFile, OrganizationFileRecord>()
            .ForMember(d => d.FileTypeName,           o => o.MapFrom(s => s.UploadFileType.Name))
            .ForMember(d => d.CreatedByDisplayName,   o => o.MapFrom(s => s.CreatedByAppUser.DisplayName))
            .ForMember(d => d.PublishedByDisplayName, o => o.MapFrom(s => s.PublishedByAppUser != null ? s.PublishedByAppUser.DisplayName : null));
    }
}
