namespace Ben.Service.Mappings.Entities;

public class UploadFileTypeExtensionProfile : Profile
{
    public UploadFileTypeExtensionProfile()
    {
        CreateMap<UploadFileTypeExtension, UploadFileTypeExtensionRecord>();
    }
}
