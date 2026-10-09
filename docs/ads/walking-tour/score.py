#!/usr/bin/env python3
"""
Music and sound for the walking tour ad (35.17 s). Every instrument and effect is a real recording:
  - strings and harp: the VSCO 2 CE samples in ~/Music/IsHaunted Strings (real sections, real harp);
  - drums: Apple's "Garage Rock Drums 02" (a recorded acoustic kit, 145 bpm), cut to the picture;
  - guitar: real distorted power-chord hits from Apple's "Combustion Rhythm Guitar" and real palm-muted
    chugs from "Gearbox Muted Guitar", re-sequenced on the race's chords;
  - synths for the future: Logic's recorded synth samples (Shelburne Road Anthem lead, Kenmare Heroic
    keys, Lime Drops pluck, Pluck Synth Bass, Fairytales Bells);
  - effects: Apple's Final Cut Pro sound effects and Apple Loops sound effects (crickets, wind, owl,
    leaves, whooshes, time-lapse, cars, city, jet-pack and UFO passes, servos, robot bleeps, computer
    data, mouse clicks, shimmers and bell cascades).
All are installed with Logic Pro / GarageBand and are royalty-free for use in productions.

One theme, in E major, and every section is made from it:
  A  0–13.2   the October evening: the theme in 6/8 on strings, pizzicato oom-pa-pa, harp
  B  13.2–20.65 the race (145 bpm, 18 beats): the theme re-cut into straight rock eighths, real drums,
              grunge guitar chugs on its chords (E E A B E A B E), galloping once the future appears
  C  20.65–28.1 the robots: the same chords as a synth arpeggio and bass under real strings; the theme on
              a synth lead; it swells as the saucer crosses (the dominant B at 26.44) and resolves at 27.27
  D  28.17–35.17 the logo: the theme's first notes on harp and bells as each piece lands, a solo violin
              as the ghost floats in, the landing chord as the wordmark arrives, and the theme's cadence
              as a wink under the tagline.
    ~/.cache/ishaunted-ad/bin/python score.py out.wav
"""
import os, sys, math, random, functools, subprocess
KIT = os.environ.get('STRINGS_KIT', os.path.expanduser('~/Music/IsHaunted Strings')); sys.path.insert(0, KIT)
import numpy as np
from scipy.io import wavfile
from strings import *

HERE = os.path.dirname(os.path.abspath(__file__))
SND = os.path.join(HERE, 'build', 'snd')
FX = '/Library/Audio/Apple Loops/Apple/Final Cut Pro Sound Effects'
LIB = os.path.expanduser('~/Music/Logic Pro Library.bundle')
DUR = 844 / 24
sc = Score(DUR)
MUS, SFX = sc.mus, sc.sfx
rng = np.random.default_rng(1009); random.seed(1009)

def play(inst, art, midi_, t0, dur, vel=.7, pan=0.0, rel=.35, att=0.0):
    sc.play(inst, art, midi_, t0, dur, vel, pan, None, rel, min(att, .9 * (dur + rel)))
def put(bus, t0, x, g=1.0, pan=0.0): sc.place(bus, t0, x, g, pan)

# ── real recordings ──────────────────────────────────────────────────────────
SOURCES = {   # name -> source file (decoded once into build/snd)
    'crickets': f'{FX}/Ambience/Crickets FX 01.caf', 'night': f'{FX}/Ambience/Country Night.caf',
    'wind': f'{FX}/Ambience/Wind 4.caf', 'owl': f'{FX}/Animals/Bird Owl.caf',
    'leaves': f'{FX}/Foley/Footsteps Sneaker Dirt Walk 1.caf',
    'whoosh13': f'{FX}/Motions & Transitions/Whoosh 13.caf', 'whoosh19': f'{FX}/Motions & Transitions/Whoosh 19.caf',
    'whoosh07': f'{FX}/Motions & Transitions/Whoosh 07.caf', 'timelapse': f'{FX}/Motions & Transitions/Time Passing.caf',
    'flange': f'{FX}/Motions & Transitions/Title Whoosh Flange 03.caf', 'flange1': f'{FX}/Motions & Transitions/Title Whoosh Flange 01.caf',
    'carpass': f'{FX}/Transportation/Auto Vintage Sports Pass.caf', 'motopass': f'{FX}/Transportation/Motorcycle Modern Pass.caf',
    'city': f'{FX}/Ambience/City 2.caf', 'traffic': f'{FX}/Ambience/Traffic City 03.caf',
    'station': f'{FX}/Sci-Fi/Space Station Ambience.caf',
    'jet1': f'{FX}/Sci-Fi/Jet Pack Pass 1.caf', 'jet2': f'{FX}/Sci-Fi/Jet Pack Pass 2.caf', 'jet3': f'{FX}/Sci-Fi/Jet Pack Pass 3.caf',
    'ufo': f'{LIB}/Apple Loops/z_Legacy/Apple Loops for GarageBand/UFO Sound Effect.caf',
    'march': f'{FX}/Misc./Marching Soldiers.caf', 'clank1': f'{FX}/Misc./Clank 1.caf', 'clank2': f'{FX}/Misc./Clank 2.caf',
    'servo': f'{FX}/Mech:Tech/Servo.caf', 'servo2': f'{FX}/Mech:Tech/Servo 2.caf', 'servo3': f'{FX}/Mech:Tech/Servo 3.caf',
    'mouse': f'{FX}/Mech:Tech/Computer Mouse.caf', 'button': f'{FX}/Mech:Tech/Button Hit FX.caf', 'clickoff': f'{FX}/Mech:Tech/Click Off FX.caf',
    'shimmer': f'{FX}/Motions & Transitions/Shimmer Motion.caf', 'cascade1': f'{FX}/Motions & Transitions/Bell Cascade 1.caf',
    'cascade2': f'{FX}/Motions & Transitions/Bell Cascade 2.caf', 'belltrans': f'{FX}/Motions & Transitions/Bell Transition.caf',
    'truth': f'{LIB}/Apple Loops/Step Reflex/Higher Truth Shimmer FX.caf', 'swish2': f'{FX}/Motions & Transitions/Swish 2.caf',
    'metalhit': f'{FX}/Motions & Transitions/Metal Hit FX 03.caf',
    'drums': f'{LIB}/Apple Loops/Session Player/Garage Rock Drums 02.caf',
    'combustion': '/Library/Audio/Apple Loops/Apple/Apple Loops for GarageBand/Combustion Rhythm Guitar.caf',
    'muted': '/Library/Audio/Apple Loops/Apple/Apple Loops for GarageBand/Gearbox Muted Guitar.caf',
    'lead3': f'{LIB}/Samples/Synthesizer/Keys/Shelburne Road Anthem Synth_C3.aif',
    'lead4': f'{LIB}/Samples/Synthesizer/Keys/Shelburne Road Anthem Synth_C4.aif',
    'keys2': f'{LIB}/Samples/Synthesizer/Keys/Kenmare Heroic Synth_C2.aif',
    'keys3': f'{LIB}/Samples/Synthesizer/Keys/Kenmare Heroic Synth_C3.aif',
    'pluck': f'{LIB}/Samples/Synthesizer/Keys/Lime Drops Synth Pluck.aif',
    'sbass': f'{LIB}/Samples/Synthesizer/Bass/Pluck Synth Bass.aif',
    'fbells': f'{LIB}/Samples/Synthesizer/Keys/Fairytales Bells Sample.aif',
}
for n in range(1, 8): SOURCES[f'bleep{n}'] = f'/Library/Audio/Apple Loops/Apple/18 Toy Box/Robot Bleep FX 0{n}.caf'
for n in range(1, 7): SOURCES[f'data{n}'] = f'{LIB}/Apple Loops/z_Legacy/iLife Sound Effects/Sci-Fi/Computer Data 0{n}.caf'

@functools.lru_cache(None)
def rec(name):
    """A recording as float stereo at 44.1 kHz."""
    os.makedirs(SND, exist_ok=True)
    wav = os.path.join(SND, f'_{name}.wav')
    if not os.path.exists(wav):
        subprocess.run(['afconvert', '-f', 'WAVE', '-d', 'LEI16@44100', '-c', '2', SOURCES[name], wav], check=True)
    sr, x = wavfile.read(wav)
    x = x.astype(np.float64) / 32768
    return x if x.ndim == 2 else np.stack([x, x], 1)

def clip(name, a, b, fi=.02, fo=.05):
    x = rec(name)[int(a * SR):int(b * SR)].copy(); n = len(x)
    if n == 0: return x
    fi_, fo_ = min(n, int(fi * SR)), min(n, int(fo * SR))
    if fi_: x[:fi_] *= np.linspace(0, 1, fi_)[:, None]
    if fo_: x[-fo_:] *= np.linspace(1, 0, fo_)[:, None]
    return x

def peak_at(x):
    e = np.abs(x).mean(1); k = int(.05 * SR); s = np.convolve(e, np.ones(k) / k, 'same'); return np.argmax(s) / SR

def repitch(x, semis):
    if abs(semis) < 1e-3: return x
    r = 2 ** (semis / 12); idx = np.arange(0, len(x) - 1, r)
    return np.stack([np.interp(idx, np.arange(len(x)), x[:, c]) for c in (0, 1)], 1)

def norm(x, peak=1.0): m = np.abs(x).max(); return x * (peak / m) if m else x
def fades(x, fi, fo):
    n = len(x); a, b = min(n, int(fi * SR)), min(n, int(fo * SR))
    if a: x[:a] *= np.linspace(0, 1, a)[:, None]
    if b: x[-b:] *= np.linspace(1, 0, b)[:, None]
    return x
def hp2(x, f): return np.stack([hp(x[:, c], f) for c in (0, 1)], 1)
def lp2(x, f): return np.stack([lp(x[:, c], f) for c in (0, 1)], 1)
def pan_sweep(x, p0, p1):
    m = x.mean(1); p = np.linspace(p0, p1, len(m)); return np.stack([m * np.sqrt((1 - p) / 2), m * np.sqrt((1 + p) / 2)], 1)

# ── real instruments played from single recorded notes ──────────────────────
class Sampler:
    def __init__(self, roots): self.roots = roots          # [(name, midi of its note)]
    def note(self, n, dur, rel=.25, att=.0):
        name, root = min(self.roots, key=lambda r: abs(r[1] - n))
        x = repitch(norm(rec(name)), n - root)
        L = int((dur + rel) * SR); x = x[:L].copy()
        if len(x) < L: x = np.vstack([x, np.zeros((L - len(x), 2))])
        e = np.ones(L); r0 = int(dur * SR)
        e[r0:] = np.linspace(1, 0, L - r0) ** 1.5
        if att: k = int(att * SR); e[:k] = np.linspace(0, 1, k) ** 2
        return x * e[:, None]
LEAD = Sampler([('lead3', 60.06), ('lead4', 72.06)])
KEYS = Sampler([('keys2', 48.32), ('keys3', 60.16)])
PLUCK = Sampler([('pluck', 65.88)])
SBASS = Sampler([('sbass', 36.03)])
BELLS = Sampler([('fbells', 62.14)])

# the guitar: a real distorted E5 hit (Combustion) under a real palm-muted chug (Gearbox), cut tight
GTR_HITS = [.737, 4.528, 5.248, 9.015, 12.121]          # E power chords in the Combustion loop
MUTE_HITS = [.348, .470, .819, .923, 4.162, 4.278, 7.970, 8.214]
def chug(semis, length, accent=1.0, k=[0]):
    k[0] += 1
    a = GTR_HITS[k[0] % len(GTR_HITS)]; b = MUTE_HITS[k[0] % len(MUTE_HITS)]
    L = int((length + .06) * SR)
    body = rec('combustion')[int((a - .004) * SR):int((a - .004) * SR) + L * 2].copy()
    mute = rec('muted')[int((b - .004) * SR):int((b - .004) * SR) + L * 2].copy()
    x = repitch(norm(body), semis)[:L] * .8 + repitch(norm(mute), semis)[:L] * .9
    e = np.exp(-np.arange(L) / SR * 11); e[-int(.04 * SR):] *= np.linspace(1, 0, int(.04 * SR))
    return lp2(x * e[:, None], 5200) * accent
def ring(semis, d):
    a = GTR_HITS[0]; x = repitch(norm(rec('combustion')[int((a - .004) * SR):int((a + .70) * SR)].copy()), semis)
    tail = rec('combustion')[int((a + .1) * SR):int((a + .7) * SR)]
    while len(x) < d * SR: x = np.vstack([x, repitch(norm(tail), semis) * .9])
    return fades(x[:int(d * SR)], .002, d * .7)

# ── the theme, in E major: (length in 6/8 eighths, note); chords E E A B E A B E ─────────
THEME = [(2, 'B4'), (1, 'E5'), (2, 'G#5'), (1, 'F#5'), (3, 'E5'), (3, 'B4'),
         (2, 'C#5'), (1, 'E5'), (2, 'A5'), (1, 'G#5'), (6, 'F#5'),
         (2, 'B4'), (1, 'E5'), (2, 'G#5'), (1, 'B5'), (2, 'C#6'), (1, 'B5'), (2, 'A5'), (1, 'F#5'),
         (2, 'G#5'), (1, 'F#5'), (2, 'E5'), (1, 'D#5'), (6, 'E5')]
CHORDS = ['E', 'E', 'A', 'B', 'E', 'A', 'B', 'E']
ROOT = {'E': m('E2'), 'A': m('A2'), 'B': m('B2')}
TONES = {'E': ('E', 'G#', 'B'), 'A': ('A', 'C#', 'E'), 'B': ('B', 'D#', 'F#')}
def theme_68(t0, e8):
    out, t = [], t0
    for n8, nm in THEME: out.append((t, n8 * e8, m(nm), n8)); t += n8 * e8
    return out
STRAIGHT = {1: 1, 2: 1, 3: 2, 6: 4}       # the same theme in straight rock eighths: one 6/8 bar -> two beats
def theme_rock(t0, e8, bars=(0, 7)):
    out, t, pos = [], t0, 0
    for n8, nm in THEME:
        bar = pos // 6; pos += n8
        if bars[0] <= bar <= bars[1]:
            d = STRAIGHT[n8] * e8; out.append((t, d, m(nm), bar)); t += d
    return out

# ════════════════════════════════════════════════════════════════════════════
# A · 0–13.2  an October evening: the theme in 6/8 on strings
# ════════════════════════════════════════════════════════════════════════════
E8A = .24; BAR = 6 * E8A; T0 = 1.6
# the pick-up: a low E pedal opening out, the harp rising through the chord
play(CB, SUS[CB], m('E2'), 0.0, 1.8, .38, -.1, att=1.3)
play(CEL, SUS[CEL], m('E3'), 0.1, 1.7, .36, -.25, att=1.3)
play(VLA, SUS[VLA], m('B3'), .4, 1.4, .28, .2, att=1.0)
play(VLN, SUS[VLN], m('G#4'), .6, 1.2, .24, .35, att=.9)
for i, n_ in enumerate(('E3', 'B3', 'E4', 'G#4', 'B4', 'E5')):
    sc.harp(m(n_), .5 + i * .14, .42, -.25 + i * .1)
for k, ch in enumerate(CHORDS):
    tb = T0 + k * BAR; r = ROOT[ch]; last = k == 7
    third, fifth = (m(TONES[ch][1] + '3'), m(TONES[ch][2] + '3'))
    if not last:
        # oom-pa-pa: cello pizzicato on the root and the fifth, the violas' chord on the off-eighths
        play(CEL, PZZ[CEL], r + 12, tb, .3, .55, -.3); play(CEL, PZZ[CEL], r + 19, tb + 3 * E8A, .3, .45, -.3)
        for e8 in (1, 2, 4, 5):
            for n_ in (third, fifth): play(VLA, PZZ[VLA], n_ + (12 if n_ < m('C4') else 0), tb + e8 * E8A, .2, .3, .25)
        play(CB, PZZ[CB], r, tb, .4, .5, -.1)
    if k >= 4:   # the second phrase grows: a held bass, a cello counter-line, the harp rolling
        play(CB, SUS[CB], r, tb, BAR if not last else 13.25 - tb, .32, -.1, att=.2)
        play(CEL, SUS[CEL], third + 12 if ch != 'E' else m('G#3'), tb, BAR if not last else 13.25 - tb, .3, -.3, att=.25)
        if not last:
            for e8, n_ in enumerate((r + 24, r + 28 if ch == 'E' else r + 31, r + 31 if ch == 'E' else r + 28, r + 36, r + 31, r + 28)):
                sc.harp(n_, tb + e8 * E8A, .2, .3)
for (t, d, n_, n8) in theme_68(T0, E8A):
    phrase2 = t >= T0 + 4 * BAR
    play(VLN, SUS[VLN], n_, t, d * .97, .5 if phrase2 else .44, .12, rel=.3, att=.03)
    if phrase2: play(VLA, SUS[VLA], n_ - 12, t, d * .97, .26, -.12, rel=.3, att=.03)
# the final bar breathes in: tremolo swell into the take-off
play(VLN, TRM[VLN], m('B4'), 12.15, 1.1, .42, .3, att=.95); play(VLN, TRM[VLN], m('E5'), 12.3, .95, .42, .4, att=.85)
play(VLA, TRM[VLA], m('G#4'), 12.15, 1.1, .38, -.1, att=.95); play(CEL, TRM[CEL], m('E3'), 12.15, 1.1, .45, -.3, att=.95)

# ════════════════════════════════════════════════════════════════════════════
# B · 13.2–20.65  the race: real drums, grunge chugs, the theme in straight eighths
# ════════════════════════════════════════════════════════════════════════════
B0 = 13.2; BEAT = 60 / 145; E8 = BEAT / 2; S16 = BEAT / 4
STOP = B0 + 18 * BEAT                                            # 20.648, where the camera slows
def drum_beats(a, n):                                            # n beats of the loop from its beat a
    return rec('drums')[int(a * BEAT * SR):int((a + n) * BEAT * SR)].copy()
put(MUS, B0, fades(drum_beats(0, 16), .002, .01), .9)
put(MUS, B0 + 16 * BEAT, fades(drum_beats(30, 2), .005, .01), .9)          # the loop's own fill
put(MUS, STOP, fades(drum_beats(0, 4), .002, 1.3), .95)                    # its crash, ringing out
SEMIS = {'E': 0, 'A': -7, 'B': -5}
chord_at = lambda beat: CHORDS[min(7, max(0, (beat - 2) // 2))] if beat >= 2 else 'E'
for b in range(18):
    t = B0 + b * BEAT; ch = chord_at(b); s = SEMIS[ch]
    gallop = t >= 16.8
    for h, (off, ln, acc) in enumerate(((0, S16 if gallop else E8, 1.0), (2, S16, .8), (3, S16, .85)) if gallop
                                       else ((0, E8, 1.0), (2, E8, .8))):
        g = chug(s, ln, acc)
        put(MUS, t + off * S16, g, .55, -.45); put(MUS, t + off * S16 + .011, chug(s, ln, acc), .5, .45)  # double-tracked
    play(CB, SPC[CB], ROOT[ch], t, E8, .5, -.1); play(CEL, SPC[CEL], ROOT[ch] + 12, t, E8, .45, -.2)
    play(CEL, SPC[CEL], ROOT[ch] + 12, t + E8, E8, .35, -.2)
for (t, d, n_, bar) in theme_rock(B0 + 2 * BEAT, E8):
    up = 12 if bar >= 4 else 0                                    # the second phrase climbs an octave
    play(VLN, SPC[VLN], n_ + up, t, d * .9, .55, .2); play(VLA, SPC[VLA], n_ - 12 + up, t, d * .9, .38, -.15)
    if d >= BEAT: play(VLN, SUS[VLN], n_ + up, t, d * .95, .3, .25, rel=.15, att=.02)
play(VLN, TRM[VLN], m('B4'), B0 + 14 * BEAT, 4 * BEAT, .38, .3, att=1.4)   # the last bar lifts into the stop
play(VLN, TRM[VLN], m('E5'), B0 + 15 * BEAT, 3 * BEAT, .38, .4, att=1.1)
# the stop: the chord rings, the strings hit it with the drums
put(MUS, STOP, ring(0, 2.2), .55, -.4); put(MUS, STOP + .012, ring(0, 2.2), .5, .4)
for ins, n_ in ((CB, 'E2'), (CEL, 'E3'), (VLA, 'B3'), (VLN, 'E5'), (VLN, 'G#5')): play(ins, SPC[ins], m(n_), STOP, .3, .75)
play(VLN, TRM[VLN], m('B5'), STOP + .05, .8, .3, .3, att=.05)

# ════════════════════════════════════════════════════════════════════════════
# C · 20.65–28.1  the robots: synth arpeggio and bass on the theme's chords, the theme on a synth lead
# ════════════════════════════════════════════════════════════════════════════
C0 = STOP + 2 * BEAT                                             # 21.476, the robots
CB_ = [C0 + k * 2 * BEAT for k in range(9)]                     # a chord every two beats; [8] = 28.10
for k, ch in enumerate(CHORDS):
    t = CB_[k]; d = 2 * BEAT; r = ROOT[ch]; tones = [m(x + '4') for x in TONES[ch]]
    quiet = .5 if 22.6 <= t < 24.2 else 1.0                      # room for the check-in's sounds
    big = 1.25 if 24.7 <= t < 27.2 else 1.0                      # the saucer
    put(MUS, t, KEYS.note(r + 24, d * .98, rel=.3, att=.08), .16 * quiet * big)
    put(MUS, t, KEYS.note(tones[1], d * .98, rel=.3, att=.08), .1 * quiet * big, .3)
    for i in range(8):                                           # sixteenth arpeggio up the chord
        n_ = [tones[0], tones[1], tones[2], tones[0] + 12][i % 4] + (12 if i >= 4 else 0)
        put(MUS, t + i * S16, PLUCK.note(n_, S16 * .9, rel=.15), .14 * quiet * big, .35 if i % 2 else -.35)
    for i in range(4):
        put(MUS, t + i * E8, SBASS.note(r + (0 if i % 2 == 0 else 12), E8 * .85, rel=.06), .3 * quiet)
    play(CEL, SUS[CEL], r + 12, t, d, .3 * quiet, -.3, att=.1); play(CB, SUS[CB], r, t, d, .28 * quiet, -.1, att=.1)
for (t, d, n_, bar) in theme_rock(C0, E8, bars=(0, 1)):          # the robots: the theme on the lead
    put(MUS, t, LEAD.note(n_, d * .92, rel=.2), .3, .1)
for (t, d, n_, bar) in theme_rock(CB_[4], E8, bars=(4, 7)):      # the saucer: lead and violins in octaves
    put(MUS, t, LEAD.note(n_, d * .92, rel=.2), .3, .1)
    play(VLN, SUS[VLN], n_ + 12, t, d * .95, .42, .25, rel=.2, att=.03); play(VLA, SUS[VLA], n_, t, d * .95, .3, -.2, rel=.2, att=.03)
play(VLN, TRM[VLN], m('F#5'), CB_[6], 2 * BEAT, .4, .3, att=.4)                     # the dominant, held
for i, n_ in enumerate(('E4', 'G#4', 'B4', 'E5', 'G#5', 'B5', 'E6')):            # into the logo
    sc.harp(m(n_), 27.6 + i * .08, .4, -.3 + i * .1)

# ════════════════════════════════════════════════════════════════════════════
# D · 28.17–35.17  the logo: a note as each piece lands, the ghost's violin, the landing, the wink
# ════════════════════════════════════════════════════════════════════════════
def ding(t, n_, g=.28, pan=0.0):
    sc.harp(n_, t, .55, pan); put(MUS, t, BELLS.note(n_ + 12, 1.2, rel=.6), g, pan)
ding(28.62, m('B4'), pan=-.3); ding(28.85, m('E5'), pan=.3)                         # the arc, the moon
for i, n_ in enumerate(('G#5', 'F#5', 'E5', 'B5')): ding(28.95 + i * .08, m(n_), .16, -.2 + i * .13)   # the windows
play(SVN, SUS[SVN], m('B5'), 29.3, .95, .26, -.15, att=.45, rel=.4)                  # the ghost floats in
play(VLN, SUS[VLN], m('G#4'), 29.3, .95, .18, .2, att=.5, rel=.3)
LAND = 30.45                                                                        # the wordmark lands
for (ins, n_, g_) in ((CB, 'E2', .5), (CEL, 'E3', .5), (VLA, 'G#3', .4), (VLA, 'B3', .36), (VLN, 'E4', .42),
                      (VLN, 'G#4', .38), (VLN, 'B4', .34), (VLN, 'E5', .32)):
    play(ins, PZZ[ins], m(n_), LAND, .5, g_ * 1.35, 0)
    play(ins, SUS[ins], m(n_), LAND + .02, 2.6, g_ * .55, 0, att=.15, rel=.9)
for i, n_ in enumerate(('E4', 'G#4', 'B4', 'E5', 'G#5', 'B5', 'E6')): sc.harp(m(n_), LAND + .03 + i * .05, .5, -.3 + i * .1)
put(MUS, LAND, BELLS.note(m('E6'), 2.0, rel=1.0), .25, .1)
for ch, (t, d) in (('E', (LAND + 2.4, 1.5)),):                                       # under the tagline
    put(MUS, t, KEYS.note(m('E3'), d, rel=.8, att=.4), .1, -.2); put(MUS, t, KEYS.note(m('B3'), d, rel=.8, att=.4), .08, .2)
WINK = 32.95                                                                        # the theme's cadence, as a wink
for i, n_ in enumerate(('G#5', 'F#5', 'E5', 'D#5')):
    play(VLN, PZZ[VLN], m(n_), WINK + i * .13, .2, .48, .2 - i * .05)
for n_ in ('E3', 'B3'): play(CEL, PZZ[CEL], m(n_), WINK + .52, .4, .5, -.25)
sc.harp(m('E5'), WINK + .52, .5, .1); sc.harp(m('E6'), WINK + .56, .45, .25)
put(MUS, WINK + .52, BELLS.note(m('E6'), 1.6, rel=.8), .2, .2)

# ════════════════════════════════════════════════════════════════════════════
# SOUND: the fall night, the race, the city of the future, the website, the saucer
# ════════════════════════════════════════════════════════════════════════════
def bed(name, a, t0, t1, g, fi=1.0, fo=1.0, hpf=None, lpf=None):
    x = clip(name, a, a + (t1 - t0), fi, fo)
    if hpf: x = hp2(x, hpf)
    if lpf: x = lp2(x, lpf)
    put(SFX, t0, x, g)
def hit(name, t, g, pan=0.0, at_peak=False, a=0.0, b=None, semis=0.0, sweep=None):
    x = rec(name); x = x[int(a * SR):int((b or len(x) / SR) * SR)].copy()
    x = repitch(x, semis) if semis else x
    if sweep: x = pan_sweep(x, *sweep)
    t0 = t - (peak_at(x) if at_peak else 0)
    if t0 < 0: x = x[int(-t0 * SR):]; t0 = 0
    put(SFX, t0, fades(x, .005, .08), g, pan)

# the October night: crickets, a field at night, a quiet wind, an owl, leaves underfoot
bed('crickets', 8.0, 0.0, 13.9, .55, fi=.8, fo=.9)
bed('night', 3.0, 0.0, 13.9, .45, fi=1.0, fo=.9)
bed('wind', 10.0, 0.0, 13.9, .5, fi=1.5, fo=1.0, lpf=2500)
hit('owl', 7.55, .35, -.35)
bed('leaves', 2.0, 4.0, 6.4, .25, fi=.3, fo=.4)
# the camera takes off: a whoosh, time racing by, cars flashing past, the flange into the future, slowing down
hit('whoosh13', 13.25, .55, sweep=(.5, -.4), at_peak=True)
bed('timelapse', 1.0, 13.3, 20.7, .32, fi=.5, fo=.6, hpf=250)
for t, pan in ((14.6, .6), (15.5, -.5), (18.4, .55), (19.6, -.6)):
    hit('carpass', t, .28, at_peak=True, a=6.5, b=11.5, sweep=(pan, -pan))
hit('flange', 16.75, .35, at_peak=True, a=0, b=3.2)
hit('whoosh19', 20.75, .4, at_peak=True)
# the future: a real city underneath, a station's hum, flying cars and a UFO across the sky
bed('traffic', 12.0, 20.55, 28.3, .32, fi=.6, fo=1.0, lpf=6000)
bed('station', 5.0, 20.55, 28.3, .1, fi=1.0, fo=1.0, lpf=3000)
hit('jet1', 21.1, .32, at_peak=True, sweep=(-.8, .8)); hit('jet2', 22.25, .26, at_peak=True, sweep=(.8, -.7))
hit('ufo', 23.65, .3, at_peak=True, sweep=(-.6, .6)); hit('jet3', 27.75, .22, at_peak=True, sweep=(-.7, .7))
# the robots: marching boots made metallic, clanks, servos as the hologram comes up, bleeps and boops
march = hp2(clip('march', 3.0, 4.35, .05, .3), 600)
put(SFX, 21.35, march, .4)
hit('clank1', 21.6, .12, -.4); hit('clank2', 22.05, .1, .5)
hit('servo3', 22.15, .3, -.3); hit('servo2', 22.55, .3, .25); hit('servo', 25.05, .2, -.2)
for t, name, pan, g in ((21.85, 'bleep2', .5, .2), (22.38, 'data1', -.4, .22), (24.35, 'bleep4', .55, .18),
                        (24.62, 'bleep7', .5, .14), (25.1, 'data3', -.5, .18), (27.45, 'bleep1', .4, .16),
                        (27.7, 'data5', -.3, .14)):
    hit(name, t, g, pan)
# the website: a click, the check, a chime, the lines arriving, the pass, admitted
hit('mouse', 22.86, .5, .25); hit('button', 22.92, .45, .2)
for i, n_ in enumerate(('E6', 'G#6', 'B6')): put(SFX, 23.38 + i * .07, BELLS.note(m(n_), .7, rel=.5), .3, .25)
for t in (23.24, 23.35, 23.44, 23.52): hit('clickoff', t, .22, .25)
hit('data6', 23.58, .2, .25)
put(SFX, 23.85, BELLS.note(m('B6'), .6, rel=.5), .26, .25); put(SFX, 23.92, BELLS.note(m('E7'), .6, rel=.5), .22, .25)
# the saucer: a jet-pack rush and a flange right to left, sparkles off its trail
hit('flange1', 26.2, .5, at_peak=True, a=0, b=4.5, sweep=(.9, -.9))
hit('jet2', 26.25, .4, at_peak=True, sweep=(.95, -.95))
hit('cascade1', 25.55, .38, -.0, sweep=(.7, -.6)); hit('cascade2', 26.15, .32, sweep=(.5, -.8))
hit('truth', 25.8, .3, sweep=(.6, -.6)); bed('shimmer', 4.0, 26.6, 28.2, .22, fi=.3, fo=1.0)
# the logo: soft metal as the pieces land, a swish for the ghost, a sparkle with the landing
hit('metalhit', 28.62, .12, -.3); hit('metalhit', 28.85, .1, .3, semis=2)
hit('swish2', 29.25, .22, -.3)
hit('belltrans', LAND - .05, .22)

# the effects' level through the film: the city and the robots up a little, the saucer up more
_t = np.arange(len(SFX)) / SR
SFX *= np.interp(_t, [0, 20.4, 21.0, 25.2, 25.6, 27.3, 27.9, DUR], [1, 1, 1.5, 1.5, 1.9, 1.9, 1.2, 1.2])[:, None]

if __name__ == '__main__':
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, 'build', 'walking-tour-score.wav')
    # one scale for both buses, so the gains above are the balance (sc.mix would normalize each on its own)
    shared = .85 / np.abs(MUS).max()
    sc.mix(out, hall=.22, room=.08, music_peak=.85, sfx_peak=np.abs(SFX).max() * shared, fade_in=.25, fade_out=.9)
    print('wrote', out, f'{DUR:.2f} s')
