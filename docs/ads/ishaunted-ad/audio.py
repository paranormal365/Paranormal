"""The score for the IsHaunted ad: dramatic and cinematic, cue by cue with the picture. Everything is
synthesised (strings, brass, choir, timpani, taiko, bells, harp) so nothing in it is licensed.

  0.00  Approach   ominous strings, timpani roll, low choir; dragon wing beats; heartbeat as he panics
  2.85  Hero       riser, impact (taiko, brass, cymbal, katana), D - Bb - C - D brass in slow motion, landing boom
  5.50  Porch      warm strings; harp and chime as the bellhop opens the door
  7.80  Foyer      eerie high choir and glass bells for the ghosts; a string stab on the double take
 10.00  Villain    a dark reveal (low brass cluster, choir, timpani, thunder) then a menacing ostinato
 14.00  Scan       the ostinato builds; a breath of silence; the check lands on a triumphant chord
 21.00  Party      a clean four-on-the-floor groove, muffled behind the doors until he walks in
 25.50  Finale     a heroic swell into the final chord as the logo lands
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
from scipy.signal import butter, sosfilt, fftconvolve
from scipy.io import wavfile

SR = 44100
DUR = 30.0
N = int(SR * DUR)
rng = np.random.default_rng(11)
SFX_L = np.zeros(N); SFX_R = np.zeros(N)
MUS_L = np.zeros(N); MUS_R = np.zeros(N)
PARTY_L = np.zeros(N); PARTY_R = np.zeros(N)

def tt(d): return np.arange(int(d * SR)) / SR
def midi(m): return 440.0 * 2 ** ((m - 69) / 12)
def bp(x, lo, hi, o=2): return sosfilt(butter(o, [max(20, lo), min(SR / 2 - 100, hi)], 'band', fs=SR, output='sos'), x)
def lp(x, f, o=2): return sosfilt(butter(o, min(f, SR / 2 - 100), 'low', fs=SR, output='sos'), x)
def hp(x, f, o=2): return sosfilt(butter(o, f, 'high', fs=SR, output='sos'), x)
def expdec(n, k): return np.exp(-np.arange(n) / SR * k)

def env(n, a=.01, r=.2):
    e = np.ones(n); na = max(1, min(n, int(a * SR))); nr = max(1, min(n - na, int(r * SR)))
    e[:na] = np.linspace(0, 1, na); e[n - nr:] *= np.linspace(1, 0, nr)
    return e

def add(t0, sig, g=1.0, pan=0.0, bus='sfx'):
    i = int(t0 * SR)
    if i >= N or len(sig) == 0: return
    sig = sig[:max(0, N - i)]
    if i < 0: sig = sig[-i:]; i = 0
    l = g * np.sqrt((1 - pan) / 2); r = g * np.sqrt((1 + pan) / 2)
    L, R = {'sfx': (SFX_L, SFX_R), 'mus': (MUS_L, MUS_R), 'party': (PARTY_L, PARTY_R)}[bus]
    L[i:i + len(sig)] += sig * l; R[i:i + len(sig)] += sig * r

def saw(f, t, ph=0.0):
    return 2 * ((f * t + ph) % 1.0) - 1

# ── instruments ────────────────────────────────────────────────────────────
def strings(ms, d, att=.6, rel=.8, bright=2200, trem=0.0):
    t = tt(d); s = np.zeros(len(t))
    for m in ms:
        f = midi(m)
        for det in (-.12, -.05, 0, .05, .12):
            vib = 1 + .004 * np.sin(2 * np.pi * (5 + det * 10) * t + rng.uniform(0, 6))
            s += saw(f * (1 + det / 100) * vib, t, rng.uniform(0, 1))
    s = lp(s, bright, 4) * env(len(t), att, rel) / (len(ms) * 5)
    if trem: s *= 1 - trem * (.5 + .5 * np.sin(2 * np.pi * 12 * t))
    return s

def brass(ms, d, att=.08, rel=.5, peak=2600):
    t = tt(d); s = np.zeros(len(t))
    for m in ms:
        f = midi(m)
        for det in (-.08, 0, .08):
            s += saw(f * (1 + det / 100) * (1 + .003 * np.sin(2 * np.pi * 5.5 * t)), t, rng.uniform(0, 1))
    # the brass "blat": a bright copy and a dark copy, crossfaded as the note opens up then settles
    bright = lp(s, peak, 2); dark = lp(s, 450, 2)
    open_ = np.minimum(1, t / max(att * 2.5, .05)) * np.exp(-t * .7)
    return (bright * open_ + dark * (1 - open_)) * env(len(t), att, rel) / (len(ms) * 3)

FORM = {'ah': ((730, 1.0), (1090, .5), (2440, .25)), 'oo': ((300, 1.0), (870, .4), (2240, .15))}
def choir(ms, d, vowel='ah', att=.8, rel=1.0):
    t = tt(d); src = np.zeros(len(t))
    for m in ms:
        f = midi(m)
        for det in (-.15, 0, .15):
            src += saw(f * (1 + det / 100) * (1 + .006 * np.sin(2 * np.pi * 5 * t + rng.uniform(0, 6))), t, rng.uniform(0, 1))
    out = sum(a * bp(src, fc * .85, fc * 1.15) for fc, a in FORM[vowel])
    return out * env(len(t), att, rel) / (len(ms) * 2)

def timpani(m, d=1.6, g=1.0):
    t = tt(d); f = midi(m) * (1 + .03 * np.exp(-t * 20))
    s = np.sin(2 * np.pi * np.cumsum(f) / SR) + .4 * np.sin(2 * np.pi * np.cumsum(f * 1.5) / SR)
    return (s * expdec(len(t), 3.2) + lp(rng.standard_normal(len(t)), 400) * expdec(len(t), 30) * .6) * g

def timp_roll(m, d, g=1.0):
    out = np.zeros(int(d * SR))
    for h in np.arange(0, d, .055):
        seg_ = timpani(m, .4); i = int(h * SR)
        out[i:i + len(seg_)] += seg_[:len(out) - i] * (h / d) ** 1.5
    return out * g

def taiko(d=1.4):
    t = tt(d); f = 95 * np.exp(-t * 6) + 42
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * expdec(len(t), 4) + lp(rng.standard_normal(len(t)), 900) * expdec(len(t), 25) * .7

def boom(d=2.5):
    t = tt(d); f = 70 * np.exp(-t * 3) + 26
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * expdec(len(t), 1.6) + lp(rng.standard_normal(len(t)), 220) * expdec(len(t), 3) * .5

def cymbal(d=3.0):
    t = tt(d); n = hp(rng.standard_normal(len(t)), 5000) + .5 * bp(rng.standard_normal(len(t)), 3000, 8000)
    return n * expdec(len(t), 1.4)

def _sweep(d, lo, hi, rise=True, segs=8):
    t = tt(d); n = rng.standard_normal(len(t)); out = np.zeros(len(t)); step = max(1, len(t) // segs)
    for k in range(segs):
        a = k / (segs - 1); fc = lo * (hi / lo) ** (a if rise else 1 - a)
        w = np.clip(1 - np.abs(np.arange(len(t)) - k * step) / step, 0, 1)
        out += bp(n, fc * .6, fc * 1.6) * w
    return t, out

def riser(d):
    t, out = _sweep(d, 300, 8000, segs=10)
    return (out + np.sin(2 * np.pi * np.cumsum(200 * (8 ** (t / d))) / SR) * .3) * (t / d) ** 2

def whoosh(d, lo=200, hi=3000, rise=True):
    t, out = _sweep(d, lo, hi, rise)
    return out * np.sin(np.pi * t / d)

def shing(d=1.4):
    t = tt(d)
    s = sum(a * np.sin(2 * np.pi * f * t) * expdec(len(t), k) for f, a, k in ((2637, 1, 3), (3951, .6, 4), (5274, .4, 6), (6644, .25, 8)))
    return s * .6 + hp(rng.standard_normal(len(t)), 4000) * expdec(len(t), 40) * .5

def bell(m, d=2.5):
    t = tt(d); f = midi(m)
    return sum(a * np.sin(2 * np.pi * f * r * t) * expdec(len(t), k) for r, a, k in ((1, 1, 1.6), (2.0, .5, 2.4), (2.76, .3, 3.5), (5.4, .15, 6))) * np.minimum(1, t / .002)

def harp(m, d=1.4):
    # a plucked string, decaying: a few partials with an exponential tail
    t = tt(d); f = midi(m)
    s = sum((1 / k) * np.sin(2 * np.pi * f * k * t) * expdec(len(t), 2.5 + k * 1.8) for k in range(1, 7))
    return s * np.minimum(1, t / .003)

def heartbeat():
    t = tt(.5); th = np.sin(2 * np.pi * 55 * t) * expdec(len(t), 14)
    out = np.zeros(int(.75 * SR)); out[:len(th)] += th; out[int(.22 * SR):int(.22 * SR) + len(th)] += th * .7
    return lp(out, 200)

def roar(d=1.0):
    t = tt(d); f0 = 85 + 20 * np.sin(2 * np.pi * 3 * t) + 30 * np.exp(-t * 2)
    src = saw(1, np.cumsum(f0) / SR) + .6 * rng.standard_normal(len(t))
    out = bp(src, 300, 900) * 1.2 + bp(src, 1000, 2400) * .6 + lp(src, 250) * .8
    return np.tanh(out * 2) * env(len(t), .06, .4) * (1 + .3 * np.sin(2 * np.pi * 28 * t))

def alarm_chirp(d=.11):
    """the dragon "roars" like a car alarm arming: one short electronic chirp. A piezo-style square tone
    that blips up from 2.2 kHz to 2.9 kHz in the first 15 ms and holds, with a crisp on and off."""
    t = tt(d)
    f = np.where(t < .015, 2200 + 700 * (t / .015), 2900.0)
    tone = np.sign(np.sin(2 * np.pi * np.cumsum(f) / SR))
    tone = bp(tone, 1500, 7000)
    return tone * np.minimum(1, t / .003) * np.minimum(1, (d - t) / .012)

def thunder(d=3.0):
    t = tt(d); n = lp(rng.standard_normal(len(t)), 350, 4)
    return n * np.minimum(1, t / .08) * expdec(len(t), 1.2) * (1 + .5 * np.sin(2 * np.pi * 7 * t))

def wingbeat():
    t = tt(.35); return lp(rng.standard_normal(len(t)), 500) * np.sin(np.pi * t / .35) ** 2 + np.sin(2 * np.pi * 60 * t) * expdec(len(t), 12) * .5

def beep(f, d):
    t = tt(d); return np.sin(2 * np.pi * f * t) * env(len(t), .002, .02)

# ════════════════════════════════════════════════════════════════════════════
# CUE A — the approach (0 - 2.85)
# ════════════════════════════════════════════════════════════════════════════
add(0.0, strings([38, 45], 3.2, att=1.2, rel=.6, bright=900), .55, 0, 'mus')
add(0.6, choir([50, 57], 2.4, 'ah', att=1.0, rel=.4), .35, 0, 'mus')
add(0.0, timp_roll(38, 2.85), .45, 0, 'mus')
t = tt(9.6); wind = bp(rng.standard_normal(len(t)), 250, 900) * (.5 + .5 * np.sin(2 * np.pi * .18 * t) ** 2)
wind *= np.minimum(1, t / 1.0) * np.minimum(1, (9.6 - t) / 1.2) * np.where(t > 5.5, .4, 1.0)
add(0, wind, .14, -.3); add(0, wind[::-1].copy(), .1, .3)
for k in range(5):
    add(.1 + k * .7, wingbeat(), .5, -.5 + k * .2)
add(1.9, strings([86, 87, 89], 1.0, att=.4, rel=.15, bright=6000, trem=.6), .22, .1, 'mus')
for k in range(2):
    add(2.0 + k * .38, heartbeat(), .6, 0)

# ════════════════════════════════════════════════════════════════════════════
# CUE B — the hero (2.85 - 5.5)
# ════════════════════════════════════════════════════════════════════════════
add(2.55, riser(.32), .45, 0)
add(2.64, alarm_chirp(), .32, .4); add(2.90, alarm_chirp(), .32, .4)
add(2.85, taiko(1.6), 1.0, 0, 'mus'); add(2.85, boom(2.2), .8, 0, 'mus')
add(2.85, cymbal(3.0), .35, .2, 'mus'); add(2.86, shing(1.4), .35, -.2)
for i, ch in enumerate(([50, 54, 57, 62], [46, 50, 53, 58], [48, 52, 55, 60], [50, 54, 57, 62, 66])):
    t0 = 2.85 + i * .62; d = .62 if i < 3 else 1.3
    add(t0, brass(ch, d + .3, att=.05, rel=.4), .55 if i < 3 else .65, 0, 'mus')
    add(t0, strings([m + 12 for m in ch], d + .3, att=.05, rel=.4, bright=4000), .28, 0, 'mus')
    add(t0, taiko(.9), .55, 0, 'mus')
add(3.45, boom(2.0), .9, 0); add(3.45, lp(rng.standard_normal(int(.6 * SR)), 1200) * expdec(int(.6 * SR), 6), .3, 0)
add(4.35, shing(1.2), .22, .1)
add(4.9, whoosh(.55, 300, 5000), .3, .2)

# ════════════════════════════════════════════════════════════════════════════
# CUE C — the porch (5.5 - 7.8)
# ════════════════════════════════════════════════════════════════════════════
add(5.4, strings([50, 57, 64, 65], 2.6, att=.5, rel=.6, bright=1800), .4, 0, 'mus')
add(5.5, choir([62, 69], 2.2, 'oo', att=.8, rel=.6), .16, 0, 'mus')
for i, m in enumerate((62, 65, 69, 72, 74, 77, 81)):
    add(6.5 + i * .06, harp(m, 1.4), .3, -.3 + i * .1, 'mus')
add(6.75, bell(86, 1.6), .1, .3, 'mus'); add(6.82, bell(93, 1.6), .08, .3, 'mus')
add(7.35, whoosh(.5, 400, 6000), .18, 0)

# ════════════════════════════════════════════════════════════════════════════
# CUE D — the foyer and the ghosts (7.8 - 10.0)
# ════════════════════════════════════════════════════════════════════════════
add(7.8, strings([38, 45], 2.4, att=.4, rel=.3, bright=700), .4, 0, 'mus')
add(8.1, choir([81, 82, 86], 1.9, 'oo', att=.5, rel=.5), .22, .2, 'mus')
for i, m in enumerate((93, 89, 96, 91, 88)):
    add(8.15 + i * .3, bell(m, 1.4), .07, float(rng.uniform(-.6, .6)), 'mus')
add(8.82, strings([62, 63, 68], .5, att=.01, rel=.3, bright=5000), .45, 0, 'mus')
add(8.82, timpani(38, 1.0), .5, 0, 'mus')
add(9.1, shing(1.2), .3, .1)
add(9.6, riser(.42), .35, 0)

# ════════════════════════════════════════════════════════════════════════════
# CUE E/F — the villain and the scan (10.0 - 21.0)
# ════════════════════════════════════════════════════════════════════════════
add(10.0, brass([26, 27, 33], 3.2, att=.03, rel=1.2, peak=1600), .8, 0, 'mus')
add(10.0, choir([50, 51, 57], 3.0, 'ah', att=.15, rel=1.0), .5, 0, 'mus')
add(10.0, timpani(26, 2.2), .9, 0, 'mus'); add(10.0, boom(2.5), .8, 0, 'mus'); add(10.02, thunder(3.0), .55, .3)
BPM = 120; E8 = 60 / BPM / 2
pattern = [38, 38, 39, 38, 38, 41, 39, 38]
t0 = 10.9; k = 0
while t0 < 18.45:
    m = pattern[k % 8]; build = (t0 - 10.9) / (18.45 - 10.9)
    add(t0, strings([m, m + 12], E8 * 1.1, att=.01, rel=.06, bright=900 + 2600 * build), .32 + .25 * build, 0, 'mus')
    if k % 4 == 0:
        add(t0, timpani(26 if k % 8 == 0 else 33, .8), .25 + .3 * build, 0, 'mus')
    t0 += E8; k += 1
add(12.8, choir([62, 63], 2.6, 'ah', att=1.2, rel=.4), .18, 0, 'mus')
add(14.0, strings([74, 75, 81], 4.45, att=2.5, rel=.1, bright=5000, trem=.5), .2, 0, 'mus')
for k in range(6):
    add(15.2 + k * .5, heartbeat(), .35 + k * .05, 0)
t = tt(1.6)
shimmer = sum(np.sin(2 * np.pi * (1800 + 900 * j) * t * (1 + .3 * t)) * expdec(len(t), 2 + j) for j in range(4)) * np.minimum(1, t / .3)
add(14.7, shimmer, .05, .4)
add(17.0, whoosh(.45, 300, 5000, rise=False), .5, 0)
add(18.2, riser(.25), .3, 0)
add(18.45, beep(1320, .12), .18, 0); add(18.55, beep(1760, .16), .18, 0)
t = tt(.16); add(18.6, np.sin(2 * np.pi * np.cumsum(260 + 1200 * t / .16) / SR) * np.sin(np.pi * t / .16), .3, 0)
add(18.6, taiko(.8), .5, 0, 'mus')
add(19.5, boom(1.6), .5, 0, 'mus'); add(19.5, cymbal(2.6), .3, 0, 'mus')
add(19.5, brass([50, 57, 62, 66, 69], 1.7, att=.04, rel=.8), .6, 0, 'mus')
add(19.5, strings([62, 66, 69, 74], 1.6, att=.04, rel=.8, bright=4500), .3, 0, 'mus')
for i, m in enumerate((86, 90, 93, 98)):
    add(19.52 + i * .05, bell(m, 1.6), .12, -.2 + i * .13, 'mus')
add(21.0, whoosh(.5, 300, 4000), .35, 0)

# ════════════════════════════════════════════════════════════════════════════
# CUE G — the party (21.0 - 25.5, ducking under the finale)
# ════════════════════════════════════════════════════════════════════════════
PB = 60 / 124; P0 = 21.15
def kick():
    t = tt(.35); f = 120 * np.exp(-t * 22) + 45
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * expdec(len(t), 9)
def clap():
    n = int(.18 * SR); return bp(rng.standard_normal(n), 900, 5000) * expdec(n, 26)
def hat():
    n = int(.05 * SR); return hp(rng.standard_normal(n), 7000) * expdec(n, 70)
def bassnote(m, d):
    t = tt(d); f = midi(m)
    return lp(np.sign(np.sin(2 * np.pi * f * t)) * .5 + np.sin(2 * np.pi * f * t), 600) * env(len(t), .004, .05)
def synth(ms, d):
    t = tt(d); s = sum(saw(midi(m), t) + saw(midi(m) * 1.005, t) for m in ms)
    return lp(s, 2200) * env(len(t), .01, .08) / (len(ms) * 2)
CH = [(62, 65, 69), (58, 62, 65), (60, 64, 67), (57, 61, 64)]
b = 0; tb = P0
while tb < DUR:
    for q in range(4):
        tq = tb + q * PB
        add(tq, kick(), .9, 0, 'party')
        if q in (1, 3): add(tq, clap(), .35, .1, 'party')
        add(tq + PB / 2, hat(), .14, .35, 'party')
    ci = b % 4
    for kk, off in enumerate((0, .75, 1.5, 2, 2.75, 3.5)):
        add(tb + off * PB, bassnote(CH[ci][0] - 24 + (12 if kk in (2, 5) else 0), PB * .7), .5, -.1, 'party')
    for off in (.5, 1.5, 2.5, 3.5):
        add(tb + off * PB, synth(CH[ci], PB * .4), .3, .2, 'party')
    tb += 4 * PB; b += 1
t_all = np.arange(N) / SR
openup = np.clip((t_all - 23.6) / .9, 0, 1)
openup = np.where(t_all > 25.5, np.clip(1 - (t_all - 25.5) / .7, 0, 1), openup)
pv = np.clip((t_all - 21.15) / .2, 0, 1) * np.where(t_all > 25.5, np.clip(1 - (t_all - 25.5) / 1.4, .2, 1), 1.0)
pv *= np.where(t_all > 27.8, np.clip(1 - (t_all - 27.8) / .6, 0, 1), 1)
PARTY_L = (PARTY_L * openup + lp(PARTY_L, 600) * (1 - openup)) * pv * .5
PARTY_R = (PARTY_R * openup + lp(PARTY_R, 600) * (1 - openup)) * pv * .5
t = tt(1.3); add(24.35, bp(rng.standard_normal(len(t)), 500, 3000) * np.minimum(1, t / .1) * expdec(len(t), 2), .16, 0)

# ════════════════════════════════════════════════════════════════════════════
# CUE H — the finale (25.5 - 30)
# ════════════════════════════════════════════════════════════════════════════
add(25.6, timp_roll(38, 2.65), .5, 0, 'mus')
add(25.6, strings([50, 57, 62], 2.7, att=1.6, rel=.1, bright=3000), .35, 0, 'mus')
for i, (ch, t0) in enumerate((([50, 57, 62], 26.0), ([53, 58, 62], 26.6), ([55, 60, 64], 27.2), ([57, 61, 64, 69], 27.75))):
    add(t0, brass(ch, .7, att=.08, rel=.3), .38 + i * .06, 0, 'mus')
add(25.85, whoosh(.6, 250, 3500), .3, -.6); add(26.05, whoosh(.6, 250, 3500), .3, .6)
for i in range(4):
    add(26.8 + i * .1, bell(91 + i * 2, .9), .1, -.2 + i * .13, 'mus')
add(27.15, whoosh(1.1, 200, 2500), .25, -.3)
add(28.25, boom(2.0), .9, 0, 'mus'); add(28.25, cymbal(1.8), .4, 0, 'mus'); add(28.25, taiko(1.2), .7, 0, 'mus')
add(28.25, brass([38, 50, 57, 62, 66, 69], 1.75, att=.03, rel=.9), .8, 0, 'mus')
add(28.25, strings([62, 66, 69, 74, 78], 1.75, att=.03, rel=.9, bright=5000), .35, 0, 'mus')
add(28.25, choir([62, 69, 74], 1.75, 'ah', att=.1, rel=.9), .3, 0, 'mus')
for i, m in enumerate((74, 78, 81, 86)):
    add(28.27 + i * .06, bell(m, 1.7), .16, -.3 + i * .2, 'mus')

# ════════════════════════════════════════════════════════════════════════════
# mix: a big hall on the score, the party drier, gentle limiting
# ════════════════════════════════════════════════════════════════════════════
ir_t = tt(2.6)
def _ir():
    x = rng.standard_normal(len(ir_t)) * expdec(len(ir_t), 2.6); x = lp(x, 5000); return x / np.sqrt((x ** 2).sum())
ML = MUS_L + .45 * fftconvolve(MUS_L, _ir())[:N]; MR = MUS_R + .45 * fftconvolve(MUS_R, _ir())[:N]
mixL = ML * .95 + SFX_L + PARTY_L; mixR = MR * .95 + SFX_R + PARTY_R
fade = np.ones(N); fn = int(.3 * SR); fade[-fn:] = np.linspace(1, 0, fn); fade[:int(.25 * SR)] = np.linspace(0, 1, int(.25 * SR))
mixL *= fade; mixR *= fade
# a broadcast-style compressor: the big hits stay big, the quiet cues come up to be heard
lvl = lp(np.maximum(np.abs(mixL), np.abs(mixR)), 6, 2)
thr = np.percentile(lvl, 60)
gain = np.where(lvl > thr, (thr / np.maximum(lvl, 1e-9)) ** .55, 1.0)
gain = lp(gain, 12, 2)
mixL *= gain; mixR *= gain
pk = max(np.abs(mixL).max(), np.abs(mixR).max())
mixL = np.tanh(mixL / pk * 1.6) / np.tanh(1.6) * .9
mixR = np.tanh(mixR / pk * 1.6) / np.tanh(1.6) * .9
from common import BUILD
os.makedirs(BUILD, exist_ok=True)
wavfile.write(os.path.join(BUILD, 'soundtrack.wav'), SR, (np.stack([mixL, mixR], 1) * 32767).astype(np.int16))
print('ok', round(float(pk), 2))
