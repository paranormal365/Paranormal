namespace Ben.Service.Mappings.Entities;

public class CmsSectionProfile : Profile
{
    public CmsSectionProfile()
    {
        CreateMap<CmsSection, CmsSectionRecord>();
    }
}
