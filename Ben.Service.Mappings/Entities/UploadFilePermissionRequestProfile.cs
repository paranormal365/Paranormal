namespace Ben.Service.Mappings.Entities;

public class UploadFilePermissionRequestProfile : Profile
{
    public UploadFilePermissionRequestProfile()
    {
        CreateMap<UploadFilePermissionRequest, UploadFilePermissionRequestRecord>();
    }
}
