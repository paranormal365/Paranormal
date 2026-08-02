namespace Ben.Data.Common.Enums;

public enum ScheduleProposalStatus
{
    Pending           = 0,  // sent to client, awaiting response
    AcceptedByClient  = 1,  // client picked a slot; Investigation auto-created
    Declined          = 2,  // client declined all proposed dates
    Countered         = 3,  // client proposed a different date/time
    Converted         = 4,  // org manually converted to an Investigation
    Withdrawn         = 5,  // org withdrew the proposal
}
