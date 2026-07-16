namespace Ben.Service.Mappings.Entities;

public class UploadFileTypeProfile : Profile
{
    public UploadFileTypeProfile()
    {
        CreateMap<UploadFileType, UploadFileTypeRecord>();
    }
}
