namespace Ben.Service.Mappings.Entities;

public class UploadFileTypeProfile : Profile
{
    public UploadFileTypeProfile()
    {
        CreateMap<UploadFileType, UploadFileTypeRecord>()
            .ForMember(dest => dest.AllowedPatterns,
                       opt => opt.MapFrom(src => src.AllowedExtensions.Select(e => e.Pattern).ToList()));
    }
}
