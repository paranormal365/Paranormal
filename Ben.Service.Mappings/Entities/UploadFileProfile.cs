namespace Ben.Service.Mappings.Entities;

public class UploadFileProfile : Profile
{
    public UploadFileProfile()
    {
        // FileData is intentionally excluded — use the download endpoint for file content
        CreateMap<UploadFile, UploadFileRecord>()
            .ForMember(dest => dest.StoragePath, opt => opt.MapFrom(src => src.StoragePath))
            .ForMember(dest => dest.FileName, opt => opt.MapFrom(src => src.FileName))
            .ForMember(dest => dest.StoredFileName, opt => opt.MapFrom(src => src.StoredFileName))
            .ForMember(dest => dest.ContentType, opt => opt.MapFrom(src => src.ContentType))
            .ForMember(dest => dest.FileSize, opt => opt.MapFrom(src => src.FileSize));
    }
}
