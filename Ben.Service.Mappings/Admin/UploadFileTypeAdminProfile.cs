namespace Ben.Service.Mappings.Admin;

public class UploadFileTypeAdminProfile : Profile
{
    public UploadFileTypeAdminProfile()
    {
        CreateMap<UploadFileType, UploadFileTypeAdminRecord>();
    }
}
