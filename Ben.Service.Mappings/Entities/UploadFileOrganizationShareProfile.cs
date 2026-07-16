namespace Ben.Service.Mappings.Entities;

public class UploadFileOrganizationShareProfile : Profile
{
    public UploadFileOrganizationShareProfile()
    {
        CreateMap<UploadFileOrganizationShare, UploadFileOrganizationShareRecord>();
    }
}
