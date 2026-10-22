"""Music and sound for the place ownership ad (72.7 s). Every instrument and effect is a real recording.

    ~/.cache/ishaunted-ad/bin/python score.py [out.wav]      # build/place-ownership-score.wav

ONE TUNE, in D major, eight beats and a pick-up:

    A3 | D4 . F#4 A4 | D5 . . C#5 | B4 A4 G4 E4 | F#4 . D4 . .        (chords D D D D G A7 D D)

and every scene plays it in the clothes of its own time and place:

  1  0.0-12.4   the final, a stadium:      a brass anthem on drums, held back while the ball comes, then the save
  2  12.4-18.3  the Sistine Chapel, 1541:  a choir in the chapel's long echo, a lute, a recorder
  3  18.3-24.0  the rubber-band ball:      bluegrass — fiddle (solo violin) on the tune, banjo rolls, guitar
  4  24.0-29.7  Bedloe's Island, 1886:     a Parisian salon waltz — violins, clarinet, basses and violas, harp
  5  29.7-35.9  the space station:         celesta, vibraphone and synth pads in orbit; the erhu takes the tune when the
                                           Great Wall comes into the binoculars, a guzheng sweeps
  6  35.9-41.8  Independence Hall, 1776:   fife and drum, a roll as Hancock runs for the desk
  7  41.8-47.5  Mars:                      analog synths — bass, arpeggio, lead
  8  47.5-53.2  the Sphinx:                oud on the tune in the Hijaz mode, darbuka, a ney
  9  53.2-59.0  the prairie dog town:      cowboy trot — harmonica, guitar, woodblock hoofbeats, dobro
 10  59.0-64.8  the operating room:        piano and strings, a TV medical drama, swelling as the camera
                                           finds the phone, fading into the ending
 E  64.8-72.7  the ending:                 the tune's notes passed from instrument to instrument as the
                                           icon builds — trumpet, choir, banjo, accordion, erhu, fife, synth,
                                           oud, harmonica, piano — then all of them together on the wordmark

Sources (installed with Logic Pro / GarageBand; royalty-free for productions): Logic's orchestral, guitar,
piano, synth and world-string samples; single notes cut from Apple Loops (banjo, fiddle, harmonica,
accordion, oud, dobro) by instruments.py; the VSCO 2 CE strings in ~/Music/IsHaunted Strings; Final Cut
Pro's sound effects. Ben's video keeps its own sound; the three silent scenes (the space station, the
hall, the Sphinx) get ambience of their own.
"""
import json, os, sys, math, subprocess
import numpy as np
from scipy.io import wavfile
from scipy.signal import fftconvolve

HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
KIT = os.path.expanduser('~/Music/IsHaunted Strings'); sys.path.insert(0, KIT)
import instruments as I
from instruments import SR, FX, LEG, AL, hp, lp, bp, hit, decode, repitch
import strings as STR
import track as TR

EV = json.load(open(os.path.join(HERE, 'build', 'events.json')))
DUR = EV['total']
END = EV['end_t0']
N = int(DUR * SR)
B = [0, 12.4, 18.33, 23.97, 29.68, 35.87, 41.77, 47.47, 53.17, 58.95, END, DUR]   # scene boundaries
rng = np.random.default_rng(2026)

# ── the tune ──────────────────────────────────────────────────────────────────
THEME = [(-.5, .5, 57), (0, 1, 62), (1, .5, 66), (1.5, .5, 69), (2, 1.5, 74), (3.5, .5, 73),
         (4, .5, 71), (4.5, .5, 69), (5, .5, 67), (5.5, .5, 64), (6, 1, 66), (7, 1.5, 62)]
CHORD = ['D', 'D', 'D', 'D', 'G', 'A7', 'D', 'D']
TONES = {'D': (62, 66, 69), 'G': (67, 71, 62), 'A7': (69, 73, 64, 67)}
ROOT = {'D': 38, 'G': 43, 'A7': 45}                     # D2, G2, A2
FIFTH = {'D': 45, 'G': 38, 'A7': 40}
HIJAZ = {64: 63, 71: 70, 73: 72, 76: 75, 59: 58, 61: 60}   # D Hijaz: E->Eb, B->Bb, C#->C

def tune(t0, beat, shift=0, mode=None, pickup=True):
    out = []
    for b, d, n in THEME:
        if b < 0 and not pickup: continue
        n = mode.get(n, n) if mode else n
        out.append((t0 + b * beat, d * beat, n + shift))
    return out


# ── the buses ─────────────────────────────────────────────────────────────────
class Bus:
    def __init__(self): self.x = np.zeros((N, 2), np.float32)
    def add(self, t, y, g=1.0, pan=0.0):
        i = int(round(t * SR))
        if i < 0: y = y[-i:]; i = 0
        if i >= N or len(y) == 0: return
        if y.ndim == 1: y = np.stack([y, y], 1)
        y = y[:N - i]
        l = g * math.sqrt((1 - pan) / 2) * math.sqrt(2); r = g * math.sqrt((1 + pan) / 2) * math.sqrt(2)
        self.x[i:i + len(y), 0] += y[:, 0] * l; self.x[i:i + len(y), 1] += y[:, 1] * r

MUS = {k: Bus() for k in range(1, 12)}      # one music bus per scene (11 = the ending), each its own room
AMB = Bus()                                  # ambience and effects for the silent scenes and the ending

def play(scene, name, midi, t, dur, vel=.7, pan=0.0, **kw):
    MUS[scene].add(t, I.get(name).note(midi, dur, 1.0, **kw), vel, pan)

def chord(scene, name, notes, t, dur, vel=.5, pan=0.0, spread=0.0, **kw):
    for k, n in enumerate(notes): play(scene, name, n, t + k * spread, dur, vel / math.sqrt(len(notes)), pan, **kw)

KITDIR = f'{LEG}/Orchestral/Orchestral Kit'
def kit(scene, f, t, g=.8, pan=0.0, bus=None):
    (bus or MUS[scene]).add(t, hit(f'{KITDIR}/{f}'), g, pan)

def sfx(name, t, g=.6, pan=0.0, a=0.0, b=None, fi=.05, fo=.3, bus=None):
    """A Final Cut Pro sound effect (or any path), trimmed a..b, faded."""
    path = name if name.startswith('/') else f'{FX}/{name}.caf'
    x = decode(path); x = x[int(a * SR):int(b * SR) if b else None].copy()
    x /= (np.abs(x).max() or 1)
    k = min(len(x), int(fi * SR)); x[:k] *= np.linspace(0, 1, k)[:, None]
    k = min(len(x), int(fo * SR)); x[len(x) - k:] *= np.linspace(1, 0, k)[:, None]
    (bus or AMB).add(t, x, g, pan)

_strings = {}
def strings(scene, inst, art, midi, t, dur, vel=.6, pan=0.0, att=0.0, rel=.4):
    """The VSCO strings, through the kit's own sample chooser."""
    if scene not in _strings: _strings[scene] = STR.Score(DUR)
    _strings[scene].play(inst, art, midi, t, dur, vel, pan, None, rel, min(att, .9 * (dur + rel)))


# ════════════════════════════════════════════════════════════════════════════
# 1 · the final (0-12.4): a brass anthem, the ball, the save
# ════════════════════════════════════════════════════════════════════════════
def s1():
    S = 1
    # the walk-up: a low pedal and a snare roll building under the crowd
    play(S, 'tuba', 38, .4, .3, .5, -.2); play(S, 'st_trombone', 50, .5, 2.9, .35, -.3, att=1.2)
    play(S, 'st_trombone', 45, .9, 2.5, .3, .2, att=1.0)
    for k in range(40):
        t = .8 + k * .066; kit(S, 'Snare/KTr_ES-L1_p.wav' if k < 26 else 'Snare/KTr_ES-L1_mf.wav', t, .12 + .3 * k / 40, .1)
    kit(S, 'Gran casa/GTr1_ES_f1.wav', 3.5, .7); kit(S, 'Piatti/BE_a-due_20Is_mf.wav', 3.5, .35, .2)
    # the tune, on horns and trumpets, as the goalie looks at his tablet (104 bpm)
    bt = 60 / 104; t0 = 3.5
    for t, d, n in tune(t0, bt):
        play(S, 'st_trombone', n, t, d * .95, .55, -.25); play(S, 'st_trumpet', n + 12 if n < 62 else n, t, d * .9, .45, .2)
    for k, ch in enumerate(CHORD):
        tb = t0 + k * bt
        play(S, 'tuba', ROOT[ch] + 12 * (ROOT[ch] < 40), tb, bt * .5, .55, -.1)
        chord(S, 'st_trombone', [n - 12 for n in TONES[ch][:3]], tb, bt * .9, .4, .25)
        kit(S, 'Gran casa/GTr1_ES_mf1.wav' if k % 2 == 0 else 'Snare/KTr_ES-L1_mf.wav', tb, .45, 0)
        kit(S, 'Snare/KTr_ES-L1_p.wav', tb + bt / 2, .2, .1)
    # the ball comes: trumpets hammer the A, a roll swells, the bass climbs
    t1 = t0 + 8 * bt
    for k in range(int((10.2 - t1) / (bt / 2))):
        t = t1 + k * bt / 2; play(S, 'st_trumpet', 69 + (k // 4), t, bt * .35, .3 + .3 * k / 8, .2)
        play(S, 'tuba', 45 + (k // 2), t, bt * .4, .4, -.1)
    for k in range(int((10.2 - t1) / .05)):
        kit(S, 'Snare/KTr_ES-L1_mf.wav', t1 + k * .05, .1 + .35 * k * .05 / (10.2 - t1), -.1)
    # THE SAVE (10.25): everything on D, the tune's last notes held high, the stadium with it
    ts = 10.25
    kit(S, 'Gran casa/GTr1_ES_ff1.wav', ts, 1.0); kit(S, 'Piatti/BE_a-due_20Is_ff.wav', ts, .7, .1)
    kit(S, 'Taiko drum/Tai_ES_fff_1_L1.wav', ts, .6, -.2)
    chord(S, 'st_trumpet', [74, 78, 81], ts, 1.4, .9, .2); chord(S, 'st_trombone', [62, 66, 69], ts, 1.6, .8, -.2)
    chord(S, 'st_trombone', [50, 57, 62], ts, 1.6, .7, .1); play(S, 'tuba', 38, ts, 1.6, .7, -.1)
    # the wink (11.5): the tune's cadence, softly, on one horn
    play(S, 'st_trombone', 66, 11.35, .35, .4, -.2); play(S, 'st_trombone', 62, 11.7, .9, .4, -.2)
    play(S, 'harp', 74, 11.7, .9, .35, .3)


# ════════════════════════════════════════════════════════════════════════════
# 2 · the Sistine Chapel, 1541 (12.4-18.3): choir, lute, recorder
# ════════════════════════════════════════════════════════════════════════════
def s2():
    S = 2; bt = 60 / 84; t0 = 12.9
    for t, d, n in tune(t0, bt):
        play(S, 'choir_f', n + 12 if n < 64 else n, t, d * 1.02, .6, -.1, att=.08)
    for k, ch in enumerate(CHORD):
        tb = t0 + k * bt
        play(S, 'choir_m', ROOT[ch] + 12, tb, bt * 1.05, .5, .15, att=.1)
        play(S, 'choir_m', TONES[ch][1] - 12, tb, bt * 1.05, .35, .3, att=.1)
        # the lute: a broken chord on the guitar, thinned in the mix to a lute's nasal pluck
        for j, n in enumerate((ROOT[ch] + 12, TONES[ch][0] - 12 + 12 * (TONES[ch][0] < 62), TONES[ch][1], TONES[ch][2])):
            play(S, 'lute', n, tb + j * bt / 4, bt * .6, .32, -.35 + .1 * j)
    # a recorder answers the tune's last bar
    for t, d, n in tune(t0, bt, 12, pickup=False)[5:]:
        play(S, 'woodflute', n, t, d, .22, .4)
    play(S, 'choir_f', 74, t0 + 8 * bt, 1.2, .45, -.1, att=.2); play(S, 'choir_m', 50, t0 + 8 * bt, 1.2, .4, .1, att=.2)


# ════════════════════════════════════════════════════════════════════════════
# 3 · the world's largest ball of rubber bands (18.3-24.0): bluegrass
# ════════════════════════════════════════════════════════════════════════════
ROLL = {'D': (57, 62, 66, 57, 62, 69, 66, 62), 'G': (55, 59, 62, 55, 59, 67, 62, 59), 'A7': (57, 61, 64, 57, 61, 67, 64, 61)}
def s3():
    S = 3; bt = 60 / 112; t0 = 18.95
    # the fiddle: the solo violin, bowed short and bright, double stops on the long notes
    strings(S, STR.SVN, STR.SPC[STR.SVN], 57, t0 - .5 * bt, .5 * bt * .9, .6, -.15)
    for t, d, n in tune(t0, bt, pickup=False):
        art = STR.SPC[STR.SVN] if d < bt else STR.SUS[STR.SVN]
        strings(S, STR.SVN, art, n, t, d * .95, .62, -.15, rel=.15)
        if d >= bt: strings(S, STR.SVN, art, n - 5 if n - 5 >= 55 else n + 4, t, d * .95, .3, -.15, rel=.15)
    for k, ch in enumerate(CHORD + ['D', 'D']):
        tb = t0 + k * bt
        for j in range(2):   # banjo: a forward roll, eighths
            play(S, 'dulcimer', ROLL[ch][(2 * k + j) % 8], tb + j * bt / 2, bt * .45, .42, .35)
        # guitar: boom on the beat (root, then the fifth), chuck on the off-beat
        play(S, 'ac_guitar', (ROOT[ch] if k % 2 == 0 else FIFTH[ch]) + 12, tb, bt * .45, .5, -.3)
        if k % 2 == 1: chord(S, 'ac_guitar', [n - 12 for n in TONES[ch][:3]] if ch != 'G' else [55, 59, 62], tb, bt * .3, .45, -.25, spread=.008)
    # the grin: a banjo run up and the last chord
    for j, n in enumerate((62, 66, 69, 69)): play(S, 'dulcimer', n, t0 + 9 * bt + j * .07, .5, .4, .35)
    chord(S, 'ac_guitar', [50, 57, 62, 66], t0 + 10 * bt, 1.0, .55, -.25, spread=.012)
    play(S, 'dulcimer', 74, t0 + 10 * bt, .9, .35, .2)


# ════════════════════════════════════════════════════════════════════════════
# 4 · Bedloe's Island, 1886 (24.0-29.7): a French musette waltz
# ════════════════════════════════════════════════════════════════════════════
WALTZ = [(-1, 1, 57), (0, 1, 62), (1, 1, 66), (2, 1, 69), (3, 2, 74), (5, 1, 73), (6, 1, 71), (7, 1, 69),
         (8, .5, 67), (8.5, .5, 64), (9, 1, 66), (10, 2, 62)]
WBARS = ['D', 'D', 'G', 'D']
def s4():
    """A Parisian salon waltz: violins on the tune, clarinet under it, basses and violas oom-pah-pah, harp."""
    S = 4; bt = 60 / 168; t0 = 24.55
    for b_, d, n in WALTZ:
        t = t0 + b_ * bt
        play(S, 'st_violins', n + 12 if n < 62 else n, t, d * bt * .95, .6, .15)
        play(S, 'clarinet', n - 12 + 12 * (n < 62), t, d * bt * .9, .25, -.3)
    for k, ch in enumerate(WBARS + ['D']):
        tb = t0 + 3 * k * bt
        if k == 2:  # bar 3: G, then the A7 on its third beat
            play(S, 'st_basses', ROOT['G'], tb, bt * .8, .6, -.1)
            chord(S, 'st_violas', [59, 62, 67], tb + bt, bt * .5, .35, .25)
            chord(S, 'st_violas', [61, 64, 67], tb + 2 * bt, bt * .5, .35, .25)
            continue
        play(S, 'st_basses', ROOT[ch], tb, bt * .8, .6, -.1)
        if k < 4:
            for j in (1, 2): chord(S, 'st_violas', [62, 66, 69] if ch == 'D' else [62, 67, 71], tb + j * bt, bt * .5, .3, .25)
    for j, n in enumerate((62, 66, 69, 74, 78, 81)): play(S, 'harp', n, t0 + 15 * bt + j * .05, 1.2, .3, -.3 + .1 * j)
    chord(S, 'st_violins', [74, 78, 81], t0 + 15 * bt, 1.4, .5, .15)
    play(S, 'st_basses', 38, t0 + 15 * bt, .8, .5, -.1)
    kit(S, 'Triangle/TG1_ES_di_f_mu_1.wav', t0 + 15 * bt, .3, .5)


# ════════════════════════════════════════════════════════════════════════════
# 5 · the space station (29.7-35.9): bells and pads in orbit; the erhu for the Great Wall
# ════════════════════════════════════════════════════════════════════════════
def s5():
    S = 5; bt = 60 / 92; t0 = 30.25
    for t0p, d, ch in ((29.75, 2.8, 'D'), (32.5, 1.4, 'G'), (33.9, 2.0, 'D')):
        chord(S, 'synthkeys', [ROOT[ch] + 12] + [n - 12 for n in TONES[ch][:3]], t0p, d, .45, 0, att=.6, rel=.8)
    play(S, 'synthbass', 38, 29.75, 2.6, .35, 0)
    notes = tune(t0, bt, 12)
    for i, (t, d, n) in enumerate(notes):
        if t < 32.45:     # in orbit: celesta, and the vibraphone an octave under it
            play(S, 'celesta', n, t, d, .5, .25 * math.sin(i)); play(S, 'vibes', n - 12, t, d, .3, -.2)
        else:   # the binoculars find the Great Wall: the erhu sings the rest of it
            play(S, 'erhu', n, t, d * 1.02, .6, -.1)
            play(S, 'pipa' if i % 2 else 'guzheng', n - 12, t, d * .6, .25, .3)
    # the guzheng's sweep as the Wall comes into focus
    for j, n in enumerate((62, 64, 66, 69, 71, 74, 76, 78, 81)):
        play(S, 'guzheng', n, 32.45 + j * .045, .9, .3, -.4 + .1 * j)
    # the thumbs-up
    chord(S, 'celesta', [74, 78, 81], 35.4, 1.0, .45); play(S, 'vibes', 62, 35.4, 1.2, .3, -.2)


# ════════════════════════════════════════════════════════════════════════════
# 6 · Independence Hall, 1776 (35.9-41.8): fife and drum
# ════════════════════════════════════════════════════════════════════════════
def s6():
    S = 6; bt = 60 / 120; t0 = 36.4
    for t, d, n in tune(t0, bt, 12):
        play(S, 'woodflute', n, t, d * .85, .55, .15)
    # the drum: the rudiments of a march — a flam on the beat, a drag before the third, rolls at the ends
    for k in range(8):
        tb = t0 + k * bt
        kit(S, 'Field drum w/Field-S_ES_mf_L1.wav', tb - .025, .25, -.15); kit(S, 'Field drum w/Field-S_ES_f_L1.wav', tb, .55, -.15)
        kit(S, 'Field drum w/Field-S_ES_p_L1.wav', tb + bt / 2, .35, -.15)
        if k % 2 == 1:
            for j in range(3): kit(S, 'Field drum w/Field-S_ES_p_L1.wav', tb + bt * .75 + j * .04, .25, -.15)
    kit(S, 'Gran casa/GTr1_ES-mufa_mf1.wav', t0, .5); kit(S, 'Gran casa/GTr1_ES-mufa_mf1.wav', t0 + 4 * bt, .5)
    # he gets up and runs for the desk (40.15): a roll, the fife trilling, a crash of a landing
    tr = t0 + 8 * bt
    for k in range(int((41.4 - tr) / .045)):
        kit(S, 'Field drum w/Field-S_ES_p_L1.wav' if k % 2 else 'Field drum w/Field-S_ES_mf_L1.wav', tr + k * .045, .2 + .25 * k * .045 / (41.4 - tr), -.15)
    for k in range(int((41.3 - tr) / .09)): play(S, 'woodflute', 86 + (2 if k % 2 else 0), tr + k * .09, .08, .3, .15)
    kit(S, 'Gran casa/GTr1_ES_f1.wav', 41.45, .6); play(S, 'woodflute', 86, 41.45, .5, .5, .15)


# ════════════════════════════════════════════════════════════════════════════
# 7 · Mars (41.8-47.5): analog synths
# ════════════════════════════════════════════════════════════════════════════
def s7():
    S = 7; bt = 60 / 100; t0 = 42.35
    for t, d, n in tune(t0, bt, 12):
        play(S, 'synthlead', n, t, d * .9, .45, .1)
    for k, ch in enumerate(CHORD + ['D']):
        tb = t0 + k * bt
        # bass and arpeggio stay within a few semitones of the notes their samples were recorded on
        play(S, 'synthbass', ROOT[ch], tb, bt * .45, .6, 0); play(S, 'synthbass', ROOT[ch], tb + bt / 2, bt * .3, .35, 0)
        arp = sorted(n + 12 * (n < 60) - 12 * (n > 72) for n in TONES[ch][:3]); arp = arp + [arp[1]]
        for j in range(4): play(S, 'pluck', arp[j], tb + j * bt / 4, bt * .22, .3, -.3 + .2 * j)
    chord(S, 'synthkeys', [50, 57, 62, 66], 41.85, 0.6, .3, 0, att=.4)
    chord(S, 'synthkeys', [50, 57, 62, 66], t0 + 8 * bt, 1.4, .45, 0, att=.05)


# ════════════════════════════════════════════════════════════════════════════
# 8 · the Sphinx (47.5-53.2): oud, darbuka, ney, in the Hijaz mode
# ════════════════════════════════════════════════════════════════════════════
def s8():
    S = 8; bt = 60 / 104; t0 = 48.45
    for t, d, n in tune(t0, bt, 0, HIJAZ):
        play(S, 'pipa', n, t, d * .9, .6, .1)                     # a real lute, plucked, with tremolo
        if d >= bt: # the oud's tremolo on the long notes
            for j in range(1, int(d / .09)): play(S, 'pipa', n, t + j * .09, .08, .2, .1)
        play(S, 'lowflute', n - 12 if n > 69 else n, t, d * 1.0, .28, -.3, att=.06)       # the ney, low and breathy
    # the darbuka: maqsum — DUM tek _ tek DUM _ tek _ — doum on the low hits, tek on the rim
    darb = I.decode(I.loops('Egyptian Nile Darbouka')[0])
    segs = I.by_onsets(darb, min_len=.06, max_len=.5)
    hits = [darb[a:b] for a, b in segs]
    if hits:
        cent = [np.abs(np.fft.rfft(h[:2048].mean(1), 2048)) @ np.arange(1025) / (np.abs(np.fft.rfft(h[:2048].mean(1), 2048)).sum() + 1e-9) for h in hits]
        order = np.argsort(cent); dum = [hits[i] for i in order[:max(1, len(order) // 3)]]; tek = [hits[i] for i in order[-max(1, len(order) // 3):]]
        pat = 'DT_TD_T_'
        for k in range(int((52.9 - t0) / (bt / 2))):
            c = pat[k % 8]
            if c == '_': continue
            h = (dum if c == 'D' else tek)[k % len(dum if c == 'D' else tek)]
            MUS[S].add(t0 + k * bt / 2, h / (np.abs(h).max() or 1), .55 if c == 'D' else .35, -.15)
    play(S, 'pipa', 50, t0 - 1.2, 1.0, .5, .1)
    for j in range(int(1.0 / .09)): play(S, 'pipa', 50, t0 - 1.2 + j * .09, .08, .18, .1)
    chord(S, 'pipa', [50, 54, 57], t0 + 8 * bt, 1.0, .5, .1, spread=.03)


# ════════════════════════════════════════════════════════════════════════════
# 9 · the prairie dog town (53.2-59.0): a cowboy trot
# ════════════════════════════════════════════════════════════════════════════
def s9():
    S = 9; bt = 60 / 100; t0 = 54.0
    for t, d, n in tune(t0, bt):
        play(S, 'steel', n, t, d * .95, .55, .2)
    for k, ch in enumerate(CHORD + ['D']):
        tb = t0 + k * bt
        play(S, 'ac_guitar', (ROOT[ch] if k % 2 == 0 else FIFTH[ch]) + 12, tb, bt * .45, .5, -.25)
        chord(S, 'ac_guitar', [n - 12 for n in TONES[ch][:3]], tb + bt / 2, bt * .3, .4, -.2, spread=.01)
        for j in range(2):   # hoofbeats on a woodblock: clip-clop
            kit(S, f'Woodblocks/HBL{4 if j else 3}_Ho_ES1_mf.wav', tb + j * bt / 2, .22, .3)
    # the dobro slides in under the long notes; the grin and the wink
    for t, n in ((54.0 - .4, 50), (t0 + 2 * bt - .1, 50), (t0 + 6 * bt - .1, 45)):
        play(S, 'st_cellos', n, t, .9, .35, -.3, att=.1)
    play(S, 'steel', 74, t0 + 8 * bt + .2, .7, .4, .2)


# ════════════════════════════════════════════════════════════════════════════
# 10 · the operating room (59.0-64.8): piano and strings, into the ending
# ════════════════════════════════════════════════════════════════════════════
def s10():
    S = 10; bt = 60 / 76; t0 = 59.5
    for t, d, n in tune(t0, bt, 12):
        play(S, 'steinway', n, t, d * 1.1, .45, .1)
    for k, ch in enumerate(CHORD):
        tb = t0 + k * bt
        play(S, 'steinway', ROOT[ch] + 12, tb, bt * 1.6, .35, -.2)
        for j, n in enumerate(TONES[ch][:3]): play(S, 'steinway', n - 12, tb + bt / 2 + j * .02, bt * .9, .18, -.1)
        play(S, 'st_cellos', ROOT[ch] + 12, tb, bt * 1.05, .35, -.3, att=.3)
        play(S, 'st_violas', TONES[ch][1] - 12 + (12 if TONES[ch][1] < 66 else 0), tb, bt * 1.05, .25, .2, att=.3)
    # the camera finds the phone (61.0): the violins swell up to it
    play(S, 'st_violins', 74, 61.0, 3.3, .45, .3, att=2.2)
    play(S, 'st_violins', 78, 62.4, 2.0, .35, -.2, att=1.2)
    play(S, 'st_basses', 38, 61.5, 3.5, .45, 0, att=1.0)
    for j, n in enumerate((62, 66, 69, 74, 78, 81)): play(S, 'harp', n, 63.2 + j * .06, 1.2, .3, -.3 + .1 * j)


# ════════════════════════════════════════════════════════════════════════════
# E · the ending (64.8-): the tune passed round the instruments, then all together
# ════════════════════════════════════════════════════════════════════════════
def ending():
    S = 11; e = END
    t = lambda k: e + k
    # the tile lands: a low drum and a harp opening upward
    kit(S, 'Gran casa/GTr1_ES_mf1.wav', t(EV['tile_land']), .5)
    for j, n in enumerate((50, 57, 62, 66)): play(S, 'harp', n, t(EV['tile']) + j * .07, 1.2, .3, -.2 + .1 * j)
    # the tune's first eight notes, each on another scene's instrument, as each piece lands
    relay = [(EV['arc_land'], 'st_trumpet', 62), (EV['moon_land'], 'choir_f', 66),
             (EV['win'][0], 'dulcimer', 69), (EV['win'][1], 'st_violins', 74), (EV['win'][2], 'erhu', 73),
             (EV['win'][3], 'woodflute', 71 + 12), (EV['ghost'] + .15, 'synthlead', 69 + 12), (EV['ghost'] + .4, 'pipa', 67),
             (EV['ghost'] + .65, 'steel', 64), (EV['face'], 'steinway', 66 + 12)]
    for k, (tt, name, n) in enumerate(relay):
        play(S, name, n, t(tt), .32 if k < 9 else .8, .55, -.5 + k * .1)
    # the wordmark: all of them on D
    tw = t(EV['word'])
    chord(S, 'st_trumpet', [74, 78, 81], tw, 2.2, .7, .2); chord(S, 'st_trombone', [62, 66, 69], tw, 2.4, .7, -.2)
    chord(S, 'choir_f', [74, 78], tw, 2.6, .5, -.1, att=.15); chord(S, 'choir_m', [50, 57], tw, 2.6, .5, .1, att=.15)
    play(S, 'tuba', 38, tw, 1.6, .6, -.1); chord(S, 'st_violas', [62, 66, 69], tw, 1.4, .3, .3)
    for j, n in enumerate((62, 66, 69, 74, 78, 81, 86)): play(S, 'harp', n, tw + j * .045, 1.6, .3, -.3 + .1 * j)
    kit(S, 'Gran casa/GTr1_ES_f1.wav', tw, .7); kit(S, 'Piatti/BE_a-due_20Is_mf.wav', tw, .5, .1)
    play(S, 'st_violins', 74, tw, 3.0, .45, .3, att=.1); play(S, 'st_cellos', 50, tw, 3.0, .45, -.3, att=.1)
    # the tagline: the tune's last bar, gently, on celesta and guitar; then the final chord
    tt = t(EV['tag']) + .4; bt = 60 / 96
    for k, (b, d, n) in enumerate(THEME[6:]):
        play(S, 'celesta', n + 12, tt + (b - 4) * bt, d * bt, .4, .2)
        play(S, 'ac_guitar', n, tt + (b - 4) * bt, d * bt, .3, -.2)
    tf = tt + 4 * bt
    chord(S, 'st_trombone', [50, 57, 62, 66], tf, DUR - tf - .3, .45, 0, att=.2)
    chord(S, 'choir_f', [69, 74], tf, DUR - tf - .3, .35, 0, att=.3)
    play(S, 'st_basses', 38, tf, DUR - tf - .3, .4, 0, att=.3)
    play(S, 'glock', 86, tf, 2.5, .3, .3)


# ════════════════════════════════════════════════════════════════════════════
# the silent scenes' sound, and the ending's effects
# ════════════════════════════════════════════════════════════════════════════
def ambience():
    # the space station (29.7-35.9): the station's hum, breathing in the helmet, the suit moving
    sfx('Sci-Fi/Space Station Ambience', 29.55, .55, 0, 0, 6.6, fi=.4, fo=.5)
    sfx('Sci-Fi/Space Ship Interior', 29.55, .25, 0, 0, 6.6, fi=.4, fo=.5)
    sfx('People/Breath Male 1', 31.0, .25, -.1, 0, 1.6)                  # the binoculars come up
    sfx('Foley/Clothes 2', 30.95, .3, .2, 0, .8)
    sfx('Mech:Tech/Camera 1', 32.4, .2, .3, 0, .4)                         # the binoculars focus
    sfx('Foley/Velcro 1', 34.0, .35, .2, 0, .7)                            # the pocket
    sfx('Foley/Clothes 3', 34.2, .3, -.2, 0, .8)
    for tt in (34.85, 35.05, 35.25): sfx('Mech:Tech/Computer Mouse', tt, .12, .1, 0, .12, fi=.002, fo=.03)
    # Independence Hall (35.9-41.8): a hall full of men, quills, a chair, running feet on wood
    sfx('Ambience/Crowded Hall 1', 35.75, .4, 0, 0, 6.1, fi=.4, fo=.5)
    sfx('Ambience/Room Tone 3', 35.75, .3, 0, 0, 6.1, fi=.4, fo=.5)
    sfx('Work:Home/Pencil Writing', 36.0, .25, -.3, 0, 1.8)                # quills at the table
    sfx('Work:Home/Pencil Writing', 37.6, .15, .4, 1.0, 2.2)
    for tt in (38.15, 38.4, 38.85, 39.2): sfx('Mech:Tech/Computer Mouse', tt, .08, 0, 0, .1, fi=.002, fo=.03)
    sfx('Foley/Clothes 1', 39.6, .35, .1, 0, .7)                           # the iPad under the book
    sfx('Work:Home/File Folder', 39.75, .3, .2, 0, .6)
    sfx('Foley/Footsteps Hard Wood Walk', 40.1, .55, 0, 0, 1.6, fo=.4)    # he runs for the desk
    # the Sphinx (47.5-53.2): desert wind, the queue talking, a fly, the booth
    sfx('Ambience/Wind 3', 47.3, .45, 0, 0, 6.0, fi=.5, fo=.5)
    sfx('People/Crowd Walla 2', 49.4, .3, .1, 0, 3.9, fi=.8, fo=.4)
    sfx('Animals/Insect Fly', 50.5, .12, .5, 0, 1.4)
    sfx('Foley/Clothes 4', 51.4, .3, -.3, 0, .6)                           # the pharaoh's arm and phone
    for tt in (51.95, 52.15): sfx('Mech:Tech/Computer Mouse', tt, .1, -.1, 0, .1, fi=.002, fo=.03)
    # the ending: the ghost's swoop, a shimmer as its face appears, a whoosh for the wordmark
    sfx('Motions & Transitions/Whoosh 13', END + EV['ghost'], .35, -.4)
    sfx('Motions & Transitions/Shimmer Motion', END + EV['face'] - .1, .3, .2)
    sfx('Motions & Transitions/Bell Cascade 1', END + EV['word'], .25, .1)


# ════════════════════════════════════════════════════════════════════════════
# rooms and the mix
# ════════════════════════════════════════════════════════════════════════════
def ir(decay, damp, pre=.0, seed=1):
    r = np.random.default_rng(seed); n = int(decay * 1.4 * SR)
    e = np.exp(-np.arange(n) / SR * 6.9 / decay)
    x = np.stack([r.standard_normal(n) * e, r.standard_normal(n) * e], 1)
    x = lp(x, damp)
    k = int(.008 * SR); x[:k] *= np.linspace(0, 1, k)[:, None]
    if pre: x = np.vstack([np.zeros((int(pre * SR), 2)), x])
    return (x / np.sqrt((x ** 2).sum(0, keepdims=True))).astype(np.float32)

# scene: (reverb seconds, damping Hz, wet)
ROOMS = {1: (2.2, 5000, .35), 2: (5.0, 4500, .6), 3: (.5, 6000, .12), 4: (.9, 5500, .18), 5: (1.4, 7000, .25),
         6: (1.6, 4000, .3), 7: (1.2, 8000, .25), 8: (.6, 6000, .15), 9: (.4, 5000, .12), 10: (1.6, 5500, .3), 11: (2.4, 6000, .32)}
# how loud each scene's music sits against its picture's own sound (louder where the scene is quiet)
LEVEL = {1: 1.0, 2: 1.0, 3: 1.0, 4: 1.0, 5: 1.0, 6: 1.0, 7: 1.0, 8: 1.0, 9: 1.0, 10: 1.0, 11: 1.4}


def window(k):
    """Each scene's music fades across the dissolves into the next."""
    a, b = B[k - 1], B[k]
    w = np.zeros(N, np.float32); t = np.arange(N) / SR
    fi, fo = (.0 if k == 1 else .25), (.35 if k < 11 else .8)
    w[(t >= a - fi) & (t <= b + fo)] = 1
    if fi: m = (t >= a - fi) & (t < a + fi); w[m] = (t[m] - (a - fi)) / (2 * fi)
    m = (t > b - fo) & (t <= b + fo); w[m] = np.clip(1 - (t[m] - (b - fo)) / (2 * fo), 0, 1)
    if k == 10: w[t > b] = 1         # the operating room's strings carry on into the ending's dark
    if k == 10: m = (t > b) & (t < b + 1.6); w[m] = 1 - (t[m] - b) / 1.6; w[t >= b + 1.6] = 0
    return w


def original():
    wav = os.path.join(HERE, 'build', 'original.wav')
    if not os.path.exists(wav):
        subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', TR.SRC, '-vn', '-ac', '2', '-ar', str(SR), wav], check=True)
    _, x = wavfile.read(wav); x = x.astype(np.float32) / 32768
    y = np.zeros((N, 2), np.float32); y[:min(N, len(x))] = x[:N]
    # the picture fades into the ending; its sound goes with it
    t = np.arange(N) / SR; f = np.clip(1 - (t - 64.15) / .65, 0, 1); y *= f[:, None]
    return y


def rms_db(x): return 20 * np.log10(np.sqrt((x ** 2).mean()) + 1e-9)


def main(out):
    for f in (s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, ending, ambience):
        print(f.__name__, flush=True); f()
    for k, sc in _strings.items(): MUS[k].x += sc.mus.astype(np.float32)
    orig = original()
    stems = os.environ.get('STEMS')
    if stems: os.makedirs(os.path.join(HERE, 'build', 'stems'), exist_ok=True)
    mix = np.zeros((N, 2), np.float32)
    amb = AMB.x.copy()
    for k in range(1, 12):
        x = MUS[k].x
        if not np.abs(x).max(): continue
        dec, damp, wet = ROOMS[k]
        h = ir(dec, damp, seed=k)
        wetx = np.stack([fftconvolve(x[:, c], h[:, c])[:N] for c in (0, 1)], 1).astype(np.float32)
        y = (x + wet * wetx) * window(k)[:, None]
        if k == 3: y = hp(y, 60)
        a, b = int(B[k - 1] * SR), int(min(B[k], DUR) * SR)
        # the music sits just under the scene's own sound, so the stadium still roars and the rover still
        # whirs, but never so low it is lost; in a silent scene it carries the scene
        target = float(np.clip(rms_db(orig[a:b]) - 1.5, -24, -19)) + 20 * np.log10(LEVEL[k])
        y = y * (10 ** (target / 20) / (np.sqrt((y[a:b] ** 2).mean()) or 1)); mix += y
        # added ambience: well under the music
        if np.abs(amb[a:b]).max() > 0:
            g = 10 ** ((target - 7) / 20) / (np.sqrt((amb[a:b] ** 2).mean()) or 1)
            amb[a:b] *= g
        if stems: wavfile.write(os.path.join(HERE, 'build', 'stems', f'scene{k}.wav'), SR, (np.clip(y, -1, 1) * 32767).astype(np.int16))
    if stems:
        for nm, y in (('amb', amb), ('orig', orig), ('music', mix)):
            wavfile.write(os.path.join(HERE, 'build', 'stems', f'{nm}.wav'), SR, (np.clip(y, -1, 1) * 32767).astype(np.int16))
    total = mix + amb + orig
    # one gentle bus: a soft knee at the top, the whole at -1 dBFS peak
    pk = np.abs(total).max() or 1
    total = np.tanh(total / pk * 1.4) / np.tanh(1.4) * .89
    t = np.arange(N) / SR; total[t > DUR - .6] *= np.clip((DUR - t[t > DUR - .6]) / .6, 0, 1)[:, None]
    wavfile.write(out, SR, (total * 32767).astype(np.int16))
    print(out)


if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, 'build', 'place-ownership-score.wav'))
