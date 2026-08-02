

# Case Concepts

## Origination

A case can be opened by the organization admin or anyone in the organization with permission to create cases.  Members can propose a case which can follow our approval process.  A case can be requested by any verified website user - a person with an account.  They will have to complete the request with required information and request at least one group to take on their case. This is how the case is created and opened.

## Client

The client, if opened by a client request, is the person who originally made the request.  They may add others to the list of clients and give them access to add information or client files of evidence, but the primary client remains the original client - unless an administrator of the organization with the case changes the primary client.

### Responsibilities

The client is responsible for documenting any new occurrences.  They should provide as much information as possible.  If others observed the occurrence, they should be included.  Either from the list of people they have added, or able to add others when documenting this occurrence.  The best way to allow others to participate at a client level would be to have the other person create an account and link that account to the case as another client for the case, but it should be possible for them to just add a person with their name, age, sex and relationship.  

The occurrence should be categorized.  It should have a detailed description.  A requirement will be the date and time of the occurrence.  They can esitmate how long it lasted or give an exact date/time it ended.  If the client has an audio file, photo, video file or other media file, they should be able to add it to the instance.  If they have more than one file, they should be able to add them as well.  All files related to a case should be saved like other files, but there should be a folder called case, then saved under the case id so all case-related files are in the same folder.  When a file is uploaded, it should be processed to pull any metadata from the file and save the metadata related to the file in a linked table which is not seen by anyone except the SuperAdmin or others the SuperAdmin gives permission.  It can be used for many purposes such as verifying date and time, mapping its location using embedded lat/lon, and any other available metadata.  Also, logged for each occurrence is the ip address and date/time it was created, but this is not visible to anyone but the SuperAdmin or others the SuperAdmin gives permission.

The client can see all previous occurrences they create or any other clients linked to the case.  They can enter these occurrences anytime. These occurrences are logged and visible in a calendar using the Telerik Calendar for Blazor UI component.  This component is on the main page for the client for their case and is where they are able to add new occurrences and they will be added to the calendar.

### Communications

The client is able to communicate with their case manager, or anyone the case manager designates as being points of contact.  If the organization wishes to provide other forms of communications besides the internal messaging system, they can choose what methods are available such as points of contacts can show their phone number or personal email address or if the group has their own primary e-mail address for clients.  This is configurable by the case manager if given permission by the organization administrators.  The primary preferred form of contact will be our internal messaging.

## Case Manager

The case manager is a member of the organization who is responsible for all management of the individual case and is assigned by the organization administration after the case has been created.  The client is not notified the case has been accepted until there is a case manager as they are also the initial point of contact.

The case manager will have the option of sending an introduction message to the client and provide any contact information they wish.  They can send any questionaires they have created on the site as attachment links to the introduction message.  They can request any collected evidence the client has already gotten.  They can ask the client to link accounts to other people who should have access to case and evidence and be able to submit evidence.  They can ask them to create basic information for others who are involved - which gets added to the client people in the case, but not linked with an account or permission to enter or view evidence unless the client has them create an account and link the case to them.  The case manager can send forms for the client to review and validate they agree with such as waivers of liability etc.

The case manager picks other available organization members to participate in the case and gives them permissions such as point of contact, secretary, lead investigator, investigator, junior investigator, researcher, technical, etc. These are examples of created named roles with their own permissions and responsibilities.  Members and responsibilities have start and end dates because they may change as a case evolves and members may leave or move.

The case manager is like the administrator, but only over the case itself.  While an administrator for the organization can change who the case manager is, until that happens or the case is closed, the case manager is in charge of the case.

## Case

The case has many pieces which all are used to comprise the case as a whole.  Because there are so many moving parts, each case will have an internal message board.  This can be stored in our internal messaging system, but will be comprised of all people in the organization who participate in the case and that organizations administrators.  This is a running message board like exists when using Microsoft Teams.  You can post a message and there are replys.  You can send messages to others individually on the case and they can reply.  

Team members may have one or more responsibilities or roles for one or more cases and they may overlap.

### Research

Research for the location, its past, its surroundings. Anything that can and should be documented as research will fall under research for a case.  One or more team members may be assigned this responsibility.

Research can include existing websites or urls. It can include images or documents.  It could be audio visual information.  All files can be stored under the case folder, and in a research folder for files.

###  Investigations

A case may have one or more investigations.  The case manager is always ultimately responsible for them being the manager.  The best approach for setting up an investigation is to send a request to the client with several proposed dates and start times.  The client can either accept one or propose another.  An investigation, using this approach, is not locked in until the client and case manager or point of contact agree on a date and time.  Since it is possible the agreement may have come from speaking with the client another way, the case manager can create the investigation date and time in the calendar - which would appear on the client's calendar as well.  The client can cancel the investigation up until 24 hours before it is to occur unless their address is more than 75 miles from the main address listed for the organization... if they are further than 75 miles, they can cancel up to 72 hours before the investigation.

When an investigation is created, the case manager or organizer can request and assign members to different roles.  Members will be able to reply Will Attend or Cannot Attend.  This adds the investigation to their calendar with all information about it including their role and responsibilities based on assignment.  They can get directions to the address from their current location and see data about the investigation.  They can also see data about the case as allowed by the case manager or organization settings.

During the investigation, the investigators are able to log any evidence or occurrences.  These logs are added at the date and time it is entered and doesn't need to have a start time, but can have a duration.  The investigator can note if they are creating the entry to know when to look at evidence they are collecting (audio or visual), if they are including the evidence by uploading it, or it is just an observation.  

Afterwards, the case manager or those allowed will enter a due date.  This will be the date and time when all evidence collected during the investigation is due and no more will be allowed to be collected.  Then, votes can be taken on evidence and occurrences if desired and the most relevant decided.  Or it could be someone is just assigned to go through all evidence and make that determination.  A date should be provided to show the client for when to expect a report.

Taking the evidence determined to be the best, someone will be assigned to put together a report using the web app (We will build a report builder as one of the final phases of the application).  For audio evidence, it will be included using the Audio Player, for video it will use a new video player built from the upcoming Ben.Video app.  The files will be pulled from the uploads and streamed when the report is viewed.  

More investigations can be scheduled and follow the same path.  These investigations help build the case.



