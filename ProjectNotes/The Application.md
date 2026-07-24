# Organization

## Definition

In our application and organization defines a Ghost Hunting group.

## Ghost Hunters

A group of ghost hunters, also known as a paranormal investigation team, is a collection of people who explore places thought to be haunted to find proof of spirits. They look for evidence, study local history, and use special tools to record strange events. Local spots with deep pasts, such as the Civil War sites on a tour are frequent focal points for local groups. 

### What do they do?

**Research history:** They read old records and interview people to learn why a place might have a spooky past.

**Use gadgets:** They set up tools like EMF meters, night vision cameras, and audio recorders to catch strange sounds or sights.

**Analyze data:** They review their videos and audio recordings after an investigation to look for unexplained anomalies

### What should our application provide for the Organization?

#### Area of Operation

The organization can use the mapping functionality of the application to designate the area where they operate. They can either specify a specific location and give a range where they operate, like within 30 miles.  Or they can specify state, city or counties they operate in and we can map that area for them.  Or they can draw their operating area on a map.  The application will index their area of operation in order to be able to show users which groups are closest who accept new member. It will also be able to show who is closest and within operating range for clients looking for a group to investigate their property - if the group is accepting new cases. Some of these terms are explained better further in the document.

They should also be able to note if they accept clients outside their operating range.

#### A repository for evidence

   1) The evidence is private but can be shared publicly or with other verified groups
   2) The evidence can be shared with individuals
   3) The evidence can be added into their CMS pages 
   4) The evidence can be stored 
   5) The evidence can be presented for people to vote on whether they agree it is proof or not

#### A messaging application for organizations to communicate

   1) They can communicate with members
   2) They can communicate with clients (which I will explain later)
   3) They can communicate with members of the team who is working on the client's case (which I will explain later)
   4) They can communicate with other groups
   5) Communications can be encrypted in order to protect the content if made private
   6) Communications can be replied to 
   7) Communications track views by count and by the identification of the person or people who viewed them.
   8) There is also a structure allowed for communications which are organized just like Twitter, Truth Social or other social media - which is not a private set of communications, but does allow for DM (direct messaging).

#### An organizational calendar

   1) Where the organization can schedule meetings
   2) Where the organization can schedule public events and outings
   3) Where the organization can schedule investigations (public, private)
   4) Where the group can schedule availablility
   5) Where they group can schedule and organize client investigations for team members who are assigned.
   6) Where administrators can schedule new types of events or meetings and schedules
   7) Where they can schedule repeated meetings, like if they meet the first Tuesday of every month, they can schedule it.
   8) Where when something is scheduled for a member or group, the member or group gets a message from the internal messaging process to accept, deny or tentively reply to the request for the meeting.
   9) Where members of the organization can look at the group calendar and see what has been assigned to them or everyone.
   10) The administrators of the organization can configure the calendar to decide which members or roles have access to see which events or private events.
   11) Also, there are roles and permissions for members to have CRUD for event types created for the organization.  It will be a flexible and customizable process.
   12) Each member will have their own custom calendar
   13) Each organization will have their own custom calendar
   14) Each client (which I will explain later) will be able to see a calendar related to them.
   15) All the schedules and calendars are created and visible using the Telerik Blazor UI Calendar 

#### Membership

   1) Each organization will manage and maintain members.

   2) Members can have one or more roles within the organization.

   3) Members must have an account in the overall application before becoming a member of an organization.

   4) Application users must submit a request to an organization for membership and when requesting:

      1) They share their true name and email in the request
      2) If they have a photo of themselves, they share it with the organization in the request
      3) They can share up to five of their files - photo, video, audio 
      4) They share the city, state and country where they live (someone may be at college so, their home address may not be exactly where they live)
      5) They will have a Telerik Blazor UI text editor to submit any information they would like to include in their request to join.
      6) An organization can create a custom list of questions a request must include when being created and the user must answer them with their request.

##### Requester Review

The owner or administrators will have the ability to assign permission to a role or member(s) who can review the requests and decide if the request is automatically approved or needs more review to be approved

  1) If not automatically approved, the person who is reviewing can move to have the application discussed between members who have the ability to approve or decline applications where it goes up for review. 
     1) All reviewers will have a chance to vote on accepting or not by a specific date and time.  
     2) When the time expires, votes are calculated between all who voted and the determination is made. 
  2) A message will be sent using the internal messaging system
     1) If approved, 
        1) The requester is notified they have become a member of the organization
        2) The verified members of the organization will be notified there is a new member and they will be sent the message with an introduction of the new member with the informaiton about the member included.
     2) If denied,
        1) The requester will be sent a message that their request was declined.
        2) When being denied, the user who is denying the application can provide a reason if they want which will be included in the message
        3) The user who is denying the application will be able to mark if the requrester can try again and provide a reason why they were denied - if the denier wants to provide one.

##### Members of the organization can be made inactive

##### Members can be deleted

When a member of the organization is deleted by an administrator, owner or someone with permission, it is a soft delete.  For all intents, they are no longer active in the organization and cannot participate.  Any evidence they have contributed will remain being owned by the organization, but their name will be replaced "throughout" with a pseudonym or some designation that it was contributed by a former member.  The actual user will retain their own copies of any evidence they contributed, but only their owned evidence.  Access to any part of the organization will be removed for that member besides their personal records they contributed.

#### Cases

A case is a collection of investigations based around a single location.  An organization can open a case.  The case has statuses which it moves through.  Typically, the flow starts one of two ways. 

##### Step One: How it originates

First, it could be proposed by a member.  Unless it is being created by someone with permission to open or accept cases, it is proposed by a member.  The proposal or request must have a mapable and mapped address. There will be a summary, which can include formatting with the ability to include images and html allowed by full use of the Telerik Blazor Editor with HTML formatting (no javascript or scripts allowed).  Also, the proposal can include actual files uploaded which is attached to the request as links with thumbnails and attached to the proposal and case.

Second, any user can create a request for an organization to investigate their home or location.  This "user" will be considered a "client" to any organization.  The user will be required to complete a request form:

- The address mapped and verified it is mapable
- Their account with their name
- Basic information about them is needed 
  - Gender (Male, Female, Not Provided)
  - Birth Year (If this is too much, maybe Adult)
- A descripton of what they are and have experienced at their house
- Any files they have for evidence
- This request is attached to the user's account...
  - It allows them to create records to attach to the request (which eventually becomes a case)
  - The records are a timeline of instances they experience 
    - The instances allow them to give an html formatted summary of what happened
    - They will be able to choose one or more categories of what they experienced like a chip list and this will grow as more types become available. It will be like object moved, knocking, saw spirit, etc. The list will not be linked to any specific organization.  It will be overall so it continues to grow the more investigations we have. There could be a top level category selector like "Audible" "Physical" "Visual" and then they select the event from the sub-category... like "Audible" -> Knocking or "Audible" -> Whispering.  But there can be multiple things they are reporting.
    - (Note) this list will also be used by investigators when they are investigating in order to document what they find or experience.
    - The client will also be able to upload any files audio, photo or video as documentation.
    - The timeline created by the client will be attached to their request - which eventually becomes the case for an organization. So it becomes the client's timeline for the case after the case is assigned or accepted by an organization.
- Once a request is created, the user will be shown a list of organizations who are
  - Accepting new clients/cases
  - Ordered by range of operation and then by those accepting clients outside their range but by closest to range
- The client can select up to 2 organizations to apply to and the first to accept and approve the case will be assigned. The other organization will just see the case has been assigned.
- This client is the primary person for the request, but allow them to create other people involved like
  - If this is a husband... 
    - Wife record which could be linked to another user account if they want to create them a login
    - Child One record similar to wife
    - Child Two record similar to Child One (maybe include names just to keep them apart and sex and year of birth)
    - Friend record which could be linked to another user account
    - Uncle record which could be linked to another user account if they want to log in and include their experiences and files of evidence
    - Anyone of these can be linked as a witness of evidence for the client's case with their own timeline to be included or linked overall with evidence if they have a user account.
    - We can also track who is related and how they are related to the primary client of the case

##### Step Two: Assigned

The organization has accepted the case and then a member of the organization is assigned to be the case manager for the client.  The purpose of the case manager is to be the point of contact for the client and other people who have user accounts and linked to the case. 

The initial process after approval:

- CMS pages are always generated when a case is accepted and approved
- This will be added to the list of cases for the organization, but the visibility of the case is managed by the case manager. It will be visible to the organization, its members and the client, but the case manager determines which pieces are visible to the public. Also, if the case manager decides to hide the true identity of the client, the case manager can choose a name or pseudonym to use where the public only sees the pseudonym instead of the actual client name or children names etc.  So, there are public and private pages.
- There will be the summary request page.
- There will be a summary investigation findings page.
- There will be a history page where researchers can document findings for research on the location.
- There will be a timeline page.
  - The timeline will contains entries by everyone involved with the case and when clicked it brings up the full description of the timeline entry. The timeline can be filtered by client or members or types of entry etc.

On public pages, there will be votes from public but also votes from the current organization members and votes by members of other organizations. Administrators and case managers will be able to see the votes but the public can only see the numbers.

The case manager will:

- Make contact with the client 
- Be able to see contact information
- Be able to see other accounts linked to the request and case
- Manage the case
- Be able to make notes on the case
- Create entries for the case timeline
- Add files for the case
- Manage the CMS for the case and permissions for making the CMS pages public and which pages are made public.
- Organize and Schedule investigations
  - Assign or request members who participate in the investigation
  - Assign roles or tasks for the members who are participating in the investigation

##### Step 3: Research

The case manager or someone they assign will do research on the location.  They will be able to document the research in the CMS including any images or web pages with documentation about the location - similar to a wiki page.

##### Step 4: Investigate (One or more times)

The case manager will organize with the client one or more dates to investigate the location.  The case manager will invite or assign members of the organization to the investigate and, if there are specific tasks that need to be assigned, the case manager will assign them to the members.

During the investigation, scientific readings will be taken and documented.  They will be able to submit these documents and readings for the timeline.  They may be as evidence or to be analyzed later.  The members can also submit images both for documenation and, if found, as evidence.  The same is true for video and audio evidence.  They may store it for later evaluation or submit it as evidence.  The date and time should be noted when it was taken.  If it is evidence, there should be a HTML description and documentation provided and it should be categorized using the same category and type as used by the client so all evidence and client-provided experiences can be sorted.

The investigators can also take audio notes and store them to review later. Also, during the investigation, they can simply make notes of any observations they have made.

While the case is open and active, the client and case participants can make notes or add evidence.  All this is managed by the case manager.

##### Step 5: Summarize and Proceed

After one or more investigations, there should be enough data collected where the case manager can present all the collected information from the CMS pages with evidence to the organization.  The organization, as a group, can decide if the case needs to remain open and continue investigations or not. The case can be continued, marked haunted and proven and make the summary visible to other organizations, made public / public with pseudonyms and redactions, transferred to another organization or closed.

# To Be Continued...
