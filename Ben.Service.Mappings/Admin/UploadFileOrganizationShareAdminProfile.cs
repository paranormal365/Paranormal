namespace Ben.Service.Mappings.Admin;

public class UploadFileOrganizationShareAdminProfile : Profile
{
    public UploadFileOrganizationShareAdminProfile()
    {
        CreateMap<UploadFileOrganizationShare, UploadFileOrganizationShareAdminRecord>();
    }
}
