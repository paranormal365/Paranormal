namespace Ben.Service.Mappings.Entities;

public class OrganizationPhoneProfile : Profile
{
    public OrganizationPhoneProfile()
    {
        CreateMap<OrganizationPhone, OrganizationPhoneRecord>();
    }
}
