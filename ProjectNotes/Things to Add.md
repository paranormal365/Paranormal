## Things to Add

> 📋 These items have been planned out in detail — see **[Feature-Roadmap.md](Feature-Roadmap.md)** for the phased implementation roadmap, including what already exists in the codebase for each area, dependencies, and build order.

### Notification & Message

1) If the user has messages, let them know how many are waiting in different areas:
   1) Messages to them in internal messaging
   2) Messages originating from their group or groups they need to read
   3) Message about or concerning a case they are an investigator for or manager for
   4) (Other cases where they have messages specific to them)
2) How and where these appear are badges - maybe color coded by urgency or age - in the most obvious locations where they should be placed, but readily available and update dynamically on the site if the user receives messages while on it.

### User Images

1) Users should be able to add a public image and a private image for their profile.  The public is displayed to everyone and the private is displayed only to their group, and, if configured, to when they communicate with clients.
2) Clients share their private profile images with a group when they have a case with the group.
3) Client people who are co-clients or attached to the case, share their private image if they have one.
4) The client, when adding a person to their witness and co-clients, can add images for the person to aid in knowing who they are.
5) The client and co-clients for a case can enter occurrences with descriptions, times, images, audio and video which will be added to the timeline.
6) Don't forget if a client wants to remain private, they get their name replaced by a chosen name or naming convention for the case.  Like Witness(A) or Client(a) etc or they could pick a name or generalization like the father, the mother, a daughter, a brother, a friend etc.

### Documentation

1) I would like to generate usage and help documents for each part of the website with images.  
2) It should be easy to understand and include all pieces available to use in the app.  
3) This should be created for each type of person. Client, Group Owner, Group Member, etc. This should be a document for each of them with an index to point them to the right page for the sections of the site.

### Future Input Format Standard

1) I would like to create a standardized format to record information taken during a case from data providers.
2) These data providers are things like external EM meters with audio recording or some other thing.  It would use a JSON standard formatting with a preamble for the data with the recorder information like manufacturer, model, serial number readings to take one time like the date time it turned on, date time first used, battery power or other similar data.  This can always be expanded, but would be the bare minimum that every new product MUST have to take readings.
3) Maybe we document how polling or entries are recorded, like by movement or time and how much it takes before it records.  It could be movement detected and time if nothing recorded. It is just how to determine how recordings taken by the device are originated.
4) If the product interacts with computers or has the ability, we might allow the user to tag where the recording session is taking place - like where on the property.
5) Each reading taken or piece of data collected should have a defined format like the date, time with precision. If available, maybe GPS coordinates, elevation, movement, direction, etc.  Depending on the product and its capabilities. We build this to accept null if the product does not provide the information.
6)  The format will be well-formed and documented in order for other providers to adopt it.

### Audit Log

1) Because this log can grow huge, it should do filtering and paging server-side.

### Audio Editing

With the audio editor, I would like to make this possibly smarter to be able to provide some way to look through long audio files for possible EVPs and mark them on the display and then the end user can look through them and determine if it is an EVP or not.  If they mark it as it is an EVP, you can give them the ability to create a clip from it and let them choose time before and after to include - if any.  It should be a GUI so they could drag start and end point on the wave form to create the region to create the clip. We should be able to allow the end user the ability to toggle things and adjust things to enhance any audio clip.

This will need to be planned out in great detail because accuracy with the audio must be very important.



## Group Type Updates

1) When we nail down all the aspects of ghost hunting, I would like to expand the group types.
   1) UFO and Unidentified Underwater Objects or whatever they are called.
   2) Bigfoot Hunting Groups
   3) Other Paranormal group types







The case page should should show the original request from the client, or the original report used to determine to take this case.  It should have a timeline with links to display what has happened, when investigations have occurred, when contact with the client(S) occurs.  When the page loads, it should be filtered by what is public and private, and what the end user who is looking at it has permission to see.

The case page is most important, but investigations and that process is also just as important.  Investigations are scheduled and managed and should have responses from members who will attend and requests to them as well.  Each investigator will have their own page for the investigation where they can document reading from instruments, upload video, images or audio.  Document findings etc.  This is kinda like their binder to keep everything together during the investigation.  So, they can go back and review it later.  The case manager or investigation manager should be able to see everything collected during the investigation and afterwards.  Not that it has to be updated, but they can request things or verify things during the investigation if they have access to the app.

---

*Device data format: specified in [ProjectNotes/specs/DeviceDataFormat-v1.md](specs/DeviceDataFormat-v1.md) (2026-08-15), with a JSON Schema and worked examples. Import not yet built.*
