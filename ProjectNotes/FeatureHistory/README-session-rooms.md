# Session rooms: which room of a place a field session was in

Branch `feature/session-rooms`, 11/21/2026.

## Why

Ben: "There is a public location called Vanderbilt. I recorded on the Paranormal Platform and want to
attach it to my room at the site. Is there a way to tag it or attach it to the location and specify a
room?"

A session could be published to a place's archive, but the room was only the free-text label the phone
stamps on each reading while recording. Nothing connected a session to the rooms a group names for a place,
and nothing could set a room afterwards.

## What was built

- **Data.** `FieldSessionUpload.PlaceRoomId` (NoAction key to `PlaceRooms`) and `RoomName` (120). Migration
  `SessionRooms`.
- **API.** `GET/PUT api/field-sessions/{id}/room` (`FieldSessionRoomController`), for the sender only:
  - It offers the place's active rooms that are public or belong to one of the sender's groups.
  - A pick sets both fields. A typed name that matches an offered room links to it; otherwise it's kept as
    typed. Both fields empty clears the room.
- **Rules that follow from the link:**
  - Renaming a room renames its sessions.
  - Removing a room that has sessions retires it (`IsActive = false`) instead of deleting it.
  - A group purge clears the link and keeps the name.
  - Publishing to another place drops the link and keeps the name.
- **Website:**
  - The session's own page has a Room picker (`SessionRoomPicker`, in `FieldSessionActionsPanel`).
  - My field sessions has a Room column that links to it.
  - The place's field archive has a Room column and a button per room, which filters the list.
  - The public playback banner says "Recorded in …".
- **iPhone:**
  - After publishing, the archive sheet asks **Which room?**
  - The new-session sheet now offers a single nearby public place as a button; before, it needed two or
    more, so one place more than a quarter mile away was never offered.
  - Two wrong sentences were fixed. Media you chose to send is shared, and only a paid plan can take a
    session back out.

## Checks

- `Ben.Web.Tests/Controllers/FieldSessionRoomTests.cs`: 9 tests on SQLite. The rename, retire and
  republish tests fail on the code before this branch.
- `BenKit` `SessionRoomTests`: 3 tests, covering decoding, the PUT, and keeping the refusal sentence.
- `Ben.Web.Playwright/Tests/SessionRoomTests.cs`: picking a group's room through to the place's page and the
  playback, and typing a room from My field sessions and clearing it.

## Not done

- The phone's own record of a session doesn't carry the room yet, so the phone's review screen doesn't show it.
