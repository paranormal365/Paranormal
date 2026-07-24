using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

public class OrganizationAreaOfOperationProfile : Profile
{
    public OrganizationAreaOfOperationProfile()
    {
        CreateMap<OrganizationAreaOfOperation, OrganizationAreaOfOperationRecord>();
    }
}
