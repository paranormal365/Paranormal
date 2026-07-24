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
                       o => o.MapFrom(s => s.VoterAppUser != null ? s.VoterAppUser.DisplayName : null));
    }
}
