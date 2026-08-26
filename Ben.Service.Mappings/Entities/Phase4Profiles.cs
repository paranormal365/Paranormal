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

public class OrganizationMemberLevelProfile : Profile
{
    public OrganizationMemberLevelProfile()
    {
        // SuggestedRoleIds is filled by the controller from a separate table (step 5), not
        // projected off the entity — said explicitly so the mapping does not quietly clear it.
        CreateMap<OrganizationMemberLevel, OrganizationMemberLevelRecord>()
            .ForMember(d => d.SuggestedRoleIds, o => o.Ignore());
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
            .ForMember(d => d.AttendeeCount,  o => o.MapFrom(s => s.Attendees.Count))
            // Flattened here so a caller rendering an event needs no second lookup just to show
            // where it is. Street and city only — the full postal form is more than a calendar row
            // can use, and the address itself is one click away.
            .ForMember(d => d.OrganizationAddressLabel, o => o.MapFrom(s =>
                s.OrganizationAddress != null
                    ? s.OrganizationAddress.StreetAddress1 + ", " + s.OrganizationAddress.City + " " + s.OrganizationAddress.State
                    : null));
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
