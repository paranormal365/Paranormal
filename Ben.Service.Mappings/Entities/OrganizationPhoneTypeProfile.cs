namespace Ben.Service.Mappings.Entities;

public class OrganizationPhoneTypeProfile : Profile
{
    public OrganizationPhoneTypeProfile()
    {
        CreateMap<OrganizationPhoneType, OrganizationPhoneTypeRecord>();
    }
}
