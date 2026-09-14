// Reading a QR code from the camera, on whatever browser is at the door (item 235 phase 7).
//
// TWO DECODERS, AND THE PLATFORM'S FIRST. Chrome, Edge and Android's WebView ship BarcodeDetector:
// it is faster, uses less battery and is already installed. Safari does not — and an iPhone is the
// single likeliest thing to be held at a door — so a vendored jsQR is fetched, once, only when the
// platform has nothing. Nobody who never opens this screen downloads a decoder, and neither does
// anybody whose browser can already decode.
//
// THE RULE IT KEEPS. It reads pixels and hands C# a string. It never creates, moves or removes a
// DOM node: the <video> and the <canvas> are Blazor's, rendered by the component, and this module
// only ever sets .srcObject and reads from them. That is what stops Blazor's diffing and the
// browser disagreeing about what is on the page.
//
// IT NEVER KEEPS THE CAMERA. Every path out — stop, an error, the component going away — stops
// every track. A camera light left on after somebody navigates away is the kind of thing a venue
// notices once and never trusts again.

const running = new Map();

/// Starts the camera and reports codes until stop() is called.
export async function start(videoId, canvasId, dotnet) {
    if (running.has(videoId)) return { ok: true, decoder: running.get(videoId).decoder };

    const video = document.getElementById(videoId);
    const canvas = document.getElementById(canvasId);
    if (!video || !canvas) return { ok: false, reason: 'nothing-to-draw-on' };

    // getUserMedia is absent, not merely refused, on an insecure origin — which is what a venue
    // testing over http://192.168.x.x hits, and the sentence has to say so rather than "no camera".
    if (!navigator.mediaDevices?.getUserMedia)
        return {
            ok: false,
            reason: window.isSecureContext ? 'no-camera-api' : 'insecure',
        };

    let stream;
    try {
        stream = await navigator.mediaDevices.getUserMedia({
            // The back camera, where there is a choice: nobody scans a pass with the selfie lens.
            video: { facingMode: { ideal: 'environment' } },
            audio: false,
        });
    } catch (e) {
        return {
            ok: false,
            reason: e?.name === 'NotAllowedError' ? 'refused'
                  : e?.name === 'NotFoundError' ? 'no-camera'
                  : 'failed',
        };
    }

    video.srcObject = stream;
    video.setAttribute('playsinline', 'true');   // iOS otherwise takes the video fullscreen
    try { await video.play(); } catch { /* autoplay policies; the frames still arrive */ }

    const detector = 'BarcodeDetector' in window
        ? new window.BarcodeDetector({ formats: ['qr_code'] })
        : null;

    const decode = detector ? null : await loadJsQr();

    if (!detector && !decode) {
        stopTracks(stream, video);
        return { ok: false, reason: 'no-decoder' };
    }

    const state = {
        stream,
        video,
        canvas,
        dotnet,
        detector,
        decode,
        timer: 0,
        last: '',
        lastAt: 0,
        decoder: detector ? 'platform' : 'jsqr',
    };
    running.set(videoId, state);

    // Four times a second. Faster reads no more codes — a phone camera cannot focus that quickly —
    // and drains a battery somebody needs for the rest of the evening.
    state.timer = setInterval(() => tick(videoId), 250);

    return { ok: true, decoder: state.decoder };
}

export function stop(videoId) {
    const state = running.get(videoId);
    if (!state) return;

    clearInterval(state.timer);
    stopTracks(state.stream, state.video);
    running.delete(videoId);
}

function stopTracks(stream, video) {
    try { stream?.getTracks()?.forEach(t => t.stop()); } catch { /* already gone */ }
    if (video) video.srcObject = null;
}

async function tick(videoId) {
    const state = running.get(videoId);
    if (!state) return;

    const { video, canvas } = state;
    if (!video.videoWidth || !video.videoHeight) return;

    let text = null;

    try {
        if (state.detector) {
            const found = await state.detector.detect(video);
            text = found?.[0]?.rawValue ?? null;
        } else {
            canvas.width = video.videoWidth;
            canvas.height = video.videoHeight;

            const context = canvas.getContext('2d', { willReadFrequently: true });
            context.drawImage(video, 0, 0, canvas.width, canvas.height);

            const pixels = context.getImageData(0, 0, canvas.width, canvas.height);
            text = state.decode(pixels.data, pixels.width, pixels.height, {
                inversionAttempts: 'dontInvert',
            })?.data ?? null;
        }
    } catch {
        // A frame that will not decode is the normal case, several times a second.
        return;
    }

    if (!text) return;

    // THE SAME CODE HELD UNDER THE CAMERA IS ONE SCAN. A pass stays in front of the lens for a
    // second or two, which is eight frames; without this the door would report the same party
    // eight times and the C# side would answer eight times over.
    const now = Date.now();
    if (text === state.last && now - state.lastAt < 3000) return;

    state.last = text;
    state.lastAt = now;

    // The component is gone by the time the promise settles if somebody navigated away mid-scan;
    // the C# side guards on its own disposed flag and this catch is the other half of that.
    try { await state.dotnet.invokeMethodAsync('CodeRead', text); } catch { stop(videoId); }
}

/// Fetches the vendored decoder, once, and only where the platform has none.
async function loadJsQr() {
    if (window.jsQR) return window.jsQR;

    await new Promise((resolve, reject) => {
        const script = document.createElement('script');
        // Root-relative on purpose: the relative form breaks under a nested route, which is every
        // route this screen has.
        script.src = '/plugins/jsqr/jsQR.js';
        script.onload = resolve;
        script.onerror = reject;
        document.head.appendChild(script);
    }).catch(() => { /* offline, or blocked; the caller falls back to typing the code */ });

    return window.jsQR ?? null;
}
