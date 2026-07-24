using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

public class OrganizationMembershipQuestionProfile : Profile
{
    public OrganizationMembershipQuestionProfile()
    {
        CreateMap<OrganizationMembershipQuestion, OrganizationMembershipQuestionRecord>();
    }
}

public class OrganizationMembershipAnswerProfile : Profile
{
    public OrganizationMembershipAnswerProfile()
    {
        CreateMap<OrganizationMembershipAnswer, OrganizationMembershipAnswerRecord>()
            .ForMember(d => d.QuestionText,
                       o => o.MapFrom(s => s.Question != null ? s.Question.QuestionText : null));
    }
}

public class MembershipReviewVoteProfile : Profile
{
    public MembershipReviewVoteProfile()
    {
        CreateMap<MembershipReviewVote, MembershipReviewVoteRecord>()
            .ForMember(d => d.VoterDisplayName,
                       o => o.MapFrom(s => s.VoterAppUser != null ? s.VoterAppUser.DisplayName : null));
    }
}
