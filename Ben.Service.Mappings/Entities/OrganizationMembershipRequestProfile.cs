using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

namespace Ben.Service.Mappings.Entities;

public class OrganizationMembershipRequestProfile : Profile
{
    public OrganizationMembershipRequestProfile()
    {
        CreateMap<OrganizationMembershipRequest, OrganizationMembershipRequestRecord>()
            .ForMember(d => d.OrganizationName,       o => o.MapFrom(s => s.Organization.Name))
            .ForMember(d => d.ApplicantDisplayName,   o => o.MapFrom(s => s.Applicant.DisplayName))
            .ForMember(d => d.ApplicantEmail,         o => o.MapFrom(s => s.Applicant.Email ?? string.Empty))
            .ForMember(d => d.RespondedByDisplayName, o => o.MapFrom(s => s.UpdatedByAppUser != null ? s.UpdatedByAppUser.DisplayName : null))
            .ForMember(d => d.DateResponded,          o => o.MapFrom(s => s.DateUpdated));
    }
}
