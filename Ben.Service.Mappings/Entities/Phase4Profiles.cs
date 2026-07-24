using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

public class OrgMessageProfile : Profile
{
    public OrgMessageProfile()
    {
        CreateMap<OrgMessage, OrgMessageRecord>()
            .ForMember(d => d.AuthorDisplayName,
                       o => o.MapFrom(s => s.AuthorAppUser != null ? s.AuthorAppUser.DisplayName : null))
            .ForMember(d => d.ReplyCount, o => o.MapFrom(s => s.Replies.Count))
            .ForMember(d => d.IsReadByCurrentUser, o => o.Ignore()); // set manually in controller
    }
}

public class OrgCalendarEventTypeProfile : Profile
{
    public OrgCalendarEventTypeProfile()
    {
        CreateMap<OrgCalendarEventType, OrgCalendarEventTypeRecord>();
    }
}

public class OrgCalendarEventProfile : Profile
{
    public OrgCalendarEventProfile()
    {
        CreateMap<OrgCalendarEvent, OrgCalendarEventRecord>()
            .ForMember(d => d.EventTypeName,  o => o.MapFrom(s => s.EventType != null ? s.EventType.Name : null))
            .ForMember(d => d.EventTypeColor, o => o.MapFrom(s => s.EventType != null ? s.EventType.ColorClass : null))
            .ForMember(d => d.CaseReference,  o => o.MapFrom(s => s.Case != null ? $"#{s.Case.CaseYear}-{s.Case.OrgCaseNumber:D3}" : null))
            .ForMember(d => d.AttendeeCount,  o => o.MapFrom(s => s.Attendees.Count));
    }
}

public class OrgCalendarEventAttendeeProfile : Profile
{
    public OrgCalendarEventAttendeeProfile()
    {
        CreateMap<OrgCalendarEventAttendee, OrgCalendarEventAttendeeRecord>()
            .ForMember(d => d.DisplayName, o => o.MapFrom(s => s.AppUser != null ? s.AppUser.DisplayName : null));
    }
}
