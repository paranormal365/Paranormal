namespace Ben.Service.Mappings.Admin;

public class UploadFileAdminProfile : Profile
{
    public UploadFileAdminProfile()
    {
        CreateMap<UploadFile, UploadFileAdminRecord>();
    }
}
