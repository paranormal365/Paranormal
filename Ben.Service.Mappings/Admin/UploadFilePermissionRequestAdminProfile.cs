namespace Ben.Service.Mappings.Admin;

public class UploadFilePermissionRequestAdminProfile : Profile
{
    public UploadFilePermissionRequestAdminProfile()
    {
        CreateMap<UploadFilePermissionRequest, UploadFilePermissionRequestAdminRecord>();
    }
}
