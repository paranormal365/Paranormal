namespace Ben.Service.Mappings.Entities;

public class OrganizationAddressMemberAccessProfile : Profile
{
    public OrganizationAddressMemberAccessProfile()
    {
        CreateMap<OrganizationAddressMemberAccess, OrganizationAddressMemberAccessRecord>();
    }
}
