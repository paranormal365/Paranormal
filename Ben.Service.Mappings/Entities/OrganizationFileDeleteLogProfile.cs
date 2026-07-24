using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

namespace Ben.Service.Mappings.Entities;

public class OrganizationFileDeleteLogProfile : Profile
{
    public OrganizationFileDeleteLogProfile()
    {
        CreateMap<OrganizationFileDeleteLog, OrganizationFileDeleteLogRecord>();
    }
}
