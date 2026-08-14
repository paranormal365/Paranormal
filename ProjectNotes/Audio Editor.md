# Audio Editor

The audio editor is a Blazor WASM editor based on the existing editor found in the Ben.Web.Library.Manage.Audio folder of the Ben project.  It uses the WaveSurfer.js library as is used in the current project.  It provides all functions available but will do so locally on the end-user's computer before they upload it to a configured server - like is done with Ben.Video.

The audio should also have a hook in order to export it into and import it into a Ben.Video - based project.

The work process of the editor is a user can load and open local - or a configured server like is done with Ben.Video.  Once loaded, they are able to play it, enhance it and perform all the current functions found in the existing editor.  The point of the editor is to load long recordings taken when performing a Ghost Hunting investigation.  It will be used to try and find what is called an EVP - Electronic Voice Phenomena.  It is listening for spirits to talk or make noises that cannot be explained by naturally occurring sounds.

This editor should be able to highlight and pull out these occurrences.  If needed, we should consider creating a process to be able to find potential EVPs.  A computer should be able to process the recording and point out potenital EVPs.

The editor should be able to make clips from the original recording.  It should be able to make non-destructive changes to the original audio and pull out these evps and remember where they occur in the original recording in order for the end user or anyone who listens to them would be able to hear the surrounding sound to get a good idea what is happening before and after they occur.  So, they will have context of the sounds.  Timestamps are important and pulling metadata for the recording is important.  Like the original version, we store the metadata without having to display it to the end user.  We may make a tab where they are able to see it at a later date, but it is only important to collect it for now.

Refer most concepts and creation of the editor by looking at the original version.  Look at the video editor we created and use concepts from there and be able to import audio and export audio to and from Ben.Video.  Also, import and export audio from this new app I think I will call Ben.Audio.  

Like Ben.Video, we will create tests and a playground for testing the visual and set up.

