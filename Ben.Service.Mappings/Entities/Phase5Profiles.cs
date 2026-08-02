using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

public class InvestigationProfile : Profile
{
    public InvestigationProfile()
    {
        CreateMap<Investigation, InvestigationRecord>()
            .ForMember(d => d.AttendeeCount, o => o.MapFrom(s => s.Attendees.Count));
    }
}

public class InvestigationAttendeeProfile : Profile
{
    public InvestigationAttendeeProfile()
    {
        CreateMap<InvestigationAttendee, InvestigationAttendeeRecord>()
            .ForMember(d => d.DisplayName, o => o.MapFrom(s => s.AppUser != null ? s.AppUser.DisplayName : null));
    }
}

public class EvidenceVoteProfile : Profile
{
    public EvidenceVoteProfile()
    {
        CreateMap<EvidenceVote, EvidenceVoteRecord>()
            .ForMember(d => d.VoterDisplayName,
                       o => o.MapFrom(s => s.VoterAppUser != null ? s.VoterAppUser.DisplayName : null))
            .ForMember(d => d.VoterOrganizationName,
                       o => o.MapFrom(s => s.VoterOrganization != null ? s.VoterOrganization.Name : null))
            .ForMember(d => d.CaseReference,
                       o => o.MapFrom(s => s.Case != null ? $"#{s.Case.CaseYear}-{s.Case.OrgCaseNumber:D3}" : null));
    }
}
