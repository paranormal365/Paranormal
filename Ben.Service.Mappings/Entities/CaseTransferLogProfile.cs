using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

public class CaseTransferLogProfile : Profile
{
    public CaseTransferLogProfile()
    {
        CreateMap<CaseTransferLog, CaseTransferLogRecord>()
            .ForMember(d => d.FromOrganizationName, o => o.MapFrom(s => s.FromOrganization != null ? s.FromOrganization.Name : null))
            .ForMember(d => d.ToOrganizationName,   o => o.MapFrom(s => s.ToOrganization   != null ? s.ToOrganization.Name   : null))
            .ForMember(d => d.ProposedByDisplayName, o => o.MapFrom(s => s.ProposedByAppUser != null ? s.ProposedByAppUser.DisplayName : null));
    }
}
