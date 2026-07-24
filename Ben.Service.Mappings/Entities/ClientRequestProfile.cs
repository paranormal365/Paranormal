using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

public class ClientRequestProfile : Profile
{
    public ClientRequestProfile()
    {
        CreateMap<ClientRequest, ClientRequestRecord>();
    }
}

public class ClientRequestOrganizationProfile : Profile
{
    public ClientRequestOrganizationProfile()
    {
        CreateMap<ClientRequestOrganization, ClientRequestOrganizationRecord>()
            .ForMember(d => d.OrganizationName, o => o.MapFrom(s => s.Organization != null ? s.Organization.Name : null));
    }
}
