namespace Ben.Service.Mappings.Entities;

public class CmsPagePermissionProfile : Profile
{
    public CmsPagePermissionProfile()
    {
        CreateMap<CmsPagePermission, CmsPagePermissionRecord>();
    }
}
