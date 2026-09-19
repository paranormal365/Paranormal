// Chunked upload client.
//
// Why this exists at all: the site is served through Cloudflare, which refuses any request body
// over 100 MB. Large files therefore leave the browser as a series of small PUTs — each cut to
// the chunk size the SERVER declares (a site setting), never a number compiled in here.
//
// Why plain JS and not the Blazor circuit: InputFile streams through SignalR in 32 KB messages,
// which makes a multi-gigabyte file both slow and a hostage of the circuit's lifetime. Here the
// bytes go browser → this site's relay endpoint → API, and the page's circuit only hears about
// progress.
//
// Auth: none of this JS ever sees a token. The circuit mints a ticket bound to one session id
// (see UploadTicketService); every request carries that ticket and the relay swaps it for the
// real token server-side.
window.benChunkedUpload = (function () {
    "use strict";

    /** Files picked in the input, kept here so the circuit only handles names and sizes. */
    let pendingFiles = [];

    /** Reads the picked files' facts for the circuit; the bytes stay in the browser. */
    function readSelection(inputId) {
        const input = document.getElementById(inputId);
        pendingFiles = input ? Array.from(input.files) : [];
        return pendingFiles.map(f => ({
            name: f.name,
            size: f.size,
            contentType: f.type || "application/octet-stream",
        }));
    }

    function clearSelection(inputId) {
        pendingFiles = [];
        const input = document.getElementById(inputId);
        if (input) input.value = "";
    }

    async function putChunk(url, blob) {
        // A chunk that fails is retried in place; only after the retries are spent does the whole
        // file fail. Waits double each time — a Wi-Fi blip and a genuinely dead link behave
        // differently under that schedule.
        const delays = [1000, 3000, 7000];
        for (let attempt = 0; ; attempt++) {
            try {
                const res = await fetch(url, { method: "PUT", body: blob });
                if (res.ok) return;
                // 4xx is the server refusing, not the network failing — retrying cannot help.
                if (res.status >= 400 && res.status < 500)
                    throw Object.assign(new Error(await res.text() || `Rejected (${res.status})`), { fatal: true });
                throw new Error(`Chunk failed (${res.status})`);
            } catch (err) {
                if (err.fatal || attempt >= delays.length) throw err;
                await new Promise(r => setTimeout(r, delays[attempt]));
            }
        }
    }

    /**
     * Uploads one previously-read file through its session.
     * The session (and the chunk size) came from the server via the circuit; sending is
     * sequential per file so resume bookkeeping stays trivial.
     */
    async function uploadFile(dotNetRef, fileIndex, session) {
        const file = pendingFiles[fileIndex];
        if (!file) throw new Error("The selected file is no longer available — pick it again.");

        const chunkBytes = session.chunkMaxBytes;
        const base = `/uploads/chunked/${session.id}`;
        const ticket = encodeURIComponent(session.ticket);
        const totalChunks = Math.max(1, Math.ceil(file.size / chunkBytes));

        // Resume: anything the server already holds is not sent again.
        let have = new Set();
        try {
            const status = await fetch(`${base}?t=${ticket}`);
            if (status.ok) (await status.json()).receivedChunks.forEach(i => have.add(i));
        } catch { /* no status is no resume — start from the beginning */ }

        let sent = 0;
        for (let i = 0; i < totalChunks; i++) {
            const start = i * chunkBytes;
            const end = Math.min(start + chunkBytes, file.size);
            if (!have.has(i)) {
                await putChunk(`${base}/chunks/${i}?t=${ticket}`, file.slice(start, end));
            }
            sent += end - start;
            await dotNetRef.invokeMethodAsync("OnChunkProgress", fileIndex, sent, file.size);
        }

        const completed = await fetch(`${base}/complete?t=${ticket}`, { method: "POST" });
        if (!completed.ok)
            throw new Error(await completed.text() || `Completing the upload failed (${completed.status})`);
        return await completed.json();
    }

    /**
     * The classic multipart path, for files chunking refuses (SVG) — same relay idea, one POST.
     */
    async function uploadClassic(fileIndex, opts) {
        const file = pendingFiles[fileIndex];
        if (!file) throw new Error("The selected file is no longer available — pick it again.");

        const form = new FormData();
        form.append("uploadFileTypeId", opts.uploadFileTypeId);
        form.append("appUserId", opts.appUserId);
        form.append("description", opts.description || "");
        form.append("isPublic", opts.isPublic ? "True" : "False");
        form.append("file", file, file.name);

        const res = await fetch(`/uploads/classic/${opts.nonce}?t=${encodeURIComponent(opts.ticket)}`,
                                { method: "POST", body: form });
        if (!res.ok) throw new Error(await res.text() || `Upload failed (${res.status})`);
        return await res.json();
    }

    return { readSelection, clearSelection, uploadFile, uploadClassic };
})();
