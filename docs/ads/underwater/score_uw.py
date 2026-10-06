"""
Sound for the underwater ad (52.5 s), on the IsHaunted strings kit.
  0–41.2   mysterious and eerie, real strings + harp: the sub's descent, the walk to the wreck, the tablet
  35–39    five EVPs, each a ghostly, garbled pirate voice: "Arrgh" "Here" "Be" "Ye" "Booty"
  41.2–46.6 the scuba dance: disco (four on the floor, octave bass, string stabs and swoops)
  46.6–47.9 the bubble wipe;  47.9–50.5 the logo's ditty;  then a soft tail
Effects: sub engine, sonar, the wake, regulator breathing, footsteps in sand, rising bubbles, the tablet.
"""
import os, sys, math, random
KIT = os.environ.get('STRINGS_KIT', os.path.expanduser('~/Music/IsHaunted Strings')); sys.path.insert(0, KIT)
import numpy as np
from scipy.io import wavfile
from scipy.signal import fftconvolve, resample_poly
from strings import *
VOICE = os.environ.get('UW_VOICE', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'build', 'voice'))
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), 'build', 'score.wav')

sc = Score(52.5); play, harp, place = sc.play, sc.harp, sc.place
MUS, SFX, N = sc.mus, sc.sfx, sc.n
rng = np.random.default_rng(21); random.seed(21)
def noise(d): return rng.standard_normal(int(d * SR))
def ex(n, k): return np.exp(-np.arange(n) / SR * k)
def add(t0, x, g=1.0, pan=0.0, bus=None): place(SFX if bus is None else bus, t0, x, g, pan)

# ════════════════════════════════════════════════════════════════════════════
# SCORE — eerie, 0–41.2 (E, with the dark half-step and the tritone)
# ════════════════════════════════════════════════════════════════════════════
play(CB, SUS[CB], m('E2') - 12, 0.0, 17.0, .55, -.1, att=3.0)
play(CEL, SUS[CEL], m('E2'), 0.4, 16.6, .45, -.25, att=3.5)
play(VLN, TRM[VLN], m('B5'), 3.0, 7.0, .14, .4, att=3.0)
play(VLN, TRM[VLN], m('C6'), 4.0, 6.0, .11, .55, att=3.0)        # the minor second: unease
play(VLA, TRM[VLA], m('A#3'), 6.5, 5.0, .2, .1, att=2.5)        # the tritone
for tm, n_ in ((2.2, m('E3')), (5.1, m('B2')), (8.4, m('F3')), (11.0, m('E3')), (13.2, m('A#2'))):
    harp(n_, tm, .4, -.3)                                          # slow drips in the dark
play(VLA, SUS[VLA], m('G4'), 10.5, 6.0, .18, .2, att=2.0)
play(VLN, SUS[VLN], m('B4'), 12.0, 4.8, .14, .35, att=2.0)
for i, n_ in enumerate((m('E4'), m('G4'), m('A#4'), m('B4'), m('D5'), m('E5'), m('G5'), m('A#5'))):  # the diver comes out
    harp(n_, 14.5 + i * .07, .35, -.2 + i * .06)
# the walk: a slow pizzicato heartbeat; a whole-tone line drifting above
t = 17.2
while t < 31.4:
    play(CB, PZZ[CB], m('E2') - 12, t, .6, .5, -.1); play(CB, PZZ[CB], m('E2') - 12, t + .28, .5, .32, -.1)
    t += 1.25
play(CEL, SUS[CEL], m('E2'), 17.2, 6.5, .32, -.25, att=2.0)
for tm, n_, d in ((18.6, m('C5'), 1.6), (20.0, m('D5'), 1.6), (21.4, m('E5'), 1.4), (22.6, m('F#5'), 1.0)):
    play(VLN, SUS[VLN], n_, tm, d, .16, .35, att=.6)
# the wreck: a low tremolo cluster swells, harp falls away
play(CEL, TRM[CEL], m('E2'), 23.4, 7.5, .42, -.3, att=2.2)
play(CEL, TRM[CEL], m('F2'), 23.8, 7.0, .3, -.15, att=2.6)
play(VLA, TRM[VLA], m('A#3'), 24.2, 6.5, .26, .15, att=2.4)
play(VLN, TRM[VLN], m('E6'), 24.5, 6.0, .1, .5, att=3.0)
for i, n_ in enumerate((m('A#5'), m('G5'), m('E5'), m('C#5'), m('A#4'), m('G4'), m('E4'), m('C#4'))):
    harp(n_, 23.5 + i * .09, .35, .3 - i * .06)
for i in range(18):                                               # the rising bubbles: high plucks
    play(SVN, PZZ[SVN], random.choice([m('B5'), m('E6'), m('F6'), m('A#5'), m('C6')]), 26.5 + i * .16 + random.uniform(-.04, .04), .3, .18, random.uniform(-.6, .6))
# the tablet: a soft ostinato, climbing a half-step at each find
steps = [(31.5, m('E3')), (35.35, m('F3')), (36.15, m('F#3')), (36.95, m('G3')), (37.75, m('G#3')), (38.55, m('A3'))]
for i, (ts, n_) in enumerate(steps):
    end = steps[i + 1][0] if i + 1 < len(steps) else 39.6
    t = ts; k = 0
    while t < end:
        play(VLA, SPC[VLA], n_ + 12 if k % 2 else n_ + 7, t, .18, .14 + .03 * i, .2)
        if k % 4 == 0: play(CEL, SPC[CEL], n_, t, .2, .24 + .03 * i, -.25)
        t += .3; k += 1
play(CB, SUS[CB], m('E2') - 12, 31.5, 8.1, .4, -.1, att=1.0)
for tm in (35.35, 36.15, 36.95, 37.75, 38.55):                     # a stinger under each voice
    play(CEL, SPC[CEL], m('E2'), tm, .3, .55, -.2); play(CB, SPC[CB], m('E2') - 12, tm, .3, .5, -.1)
    play(VLN, TRM[VLN], m('A#5'), tm, .7, .2, .45, att=.05); play(VLN, TRM[VLN], m('B5'), tm, .7, .16, .55, att=.05)
# done: an uneasy "found it" chord, then a swell up into the dance
for (ins, n_, g_) in ((CEL, m('E3'), .35), (VLA, m('G#3') + 12, .3), (VLN, m('B4'), .3), (VLN, m('E5'), .26)):
    play(ins, SUS[ins], n_, 39.7, 1.2, g_, 0, att=.1)
for (ins, n_) in ((CEL, m('A2')), (VLA, m('E4')), (VLN, m('A4')), (VLN, m('C#5'))):
    play(ins, TRM[ins], n_, 40.35, .85, .45, 0, att=.8, rel=.05)
for i, n_ in enumerate((m('A3'), m('C#4'), m('E4'), m('A4'), m('C#5'), m('E5'), m('A5'))):
    harp(n_, 40.75 + i * .055, .45, -.3 + i * .1)

# ════════════════════════════════════════════════════════════════════════════
# THE EVPs — ghostly, garbled pirate voices
# ════════════════════════════════════════════════════════════════════════════
def lp_(x, f): return lp(x, f)
def ghost_voice(i, semis=-4):
    sr, x = wavfile.read(os.path.join(VOICE, f'raw_{i}.wav')); x = x.astype(float); x /= np.abs(x).max() or 1
    nz = np.flatnonzero(np.abs(x) > .04); x = x[max(0, nz[0] - 400):nz[-1] + 1200]
    x = resample_poly(x, 2, 1)                                     # to 44.1 kHz
    ratio = 2 ** (semis / 12); idx = np.arange(0, len(x) - 1, ratio)
    x = np.interp(idx, np.arange(len(x)), x)                       # lower and slower: a big old sea dog
    # garble: grain jitter and the odd stutter
    g = int(.035 * SR); out = []
    for k in range(0, len(x) - g, g):
        piece = x[k:k + g] * np.hanning(g)
        out.append(piece)
        if random.random() < .14: out.append(piece * .7)
    y = np.zeros(len(out) * g // 2 + g)
    for j, piece in enumerate(out): y[j * g // 2:j * g // 2 + g] += piece
    t_ = np.arange(len(y)) / SR
    y = y * (1 + .35 * np.sin(2 * np.pi * 31 * t_))                # a ghostly ring
    y = bp(y, 180, 2800)
    d = (.004 + .003 * np.sin(2 * np.pi * .8 * t_)) * SR           # underwater flange
    yd = np.interp(np.arange(len(y)) - d, np.arange(len(y)), y)
    y = y * .7 + yd * .55
    env = np.convolve(np.abs(y), np.ones(400) / 400, 'same')
    whisper = bp(rng.standard_normal(len(y)), 900, 4200) * env * 1.6
    y = lp(y, 2600) + whisper * .45
    y /= np.abs(y).max() or 1
    return y
def verb_tail(x, d=3.0, k=1.6):
    n = int(d * SR); ir = rng.standard_normal(n) * ex(n, k); ir = lp(ir, 3500); ir /= np.sqrt((ir ** 2).sum())
    return fftconvolve(x, ir)[:len(x) + n]
for i, tm in enumerate((35.35, 36.15, 36.95, 37.75, 38.55), 1):
    v = ghost_voice(i, semis=-5 if i in (1, 5) else -4)
    wet = verb_tail(v)
    pre = wet[:int(.6 * SR)][::-1] * np.linspace(0, 1, int(.6 * SR)) ** 2   # a reversed breath before it
    add(tm - .62, pre, .32, .1 * (i - 3))
    add(tm - .05, v, .9, .1 * (i - 3)); add(tm - .05, wet, .32, -.1 * (i - 3))

# ════════════════════════════════════════════════════════════════════════════
# THE DISCO — 41.2–46.6, 120 bpm in A minor
# ════════════════════════════════════════════════════════════════════════════
D0, BPM = 41.2, 120; B4 = 60 / BPM
def kick():
    t_ = tt(.32); f = 110 * np.exp(-t_ * 28) + 48
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * ex(len(t_), 9) + bp(noise(.32), 2000, 6000) * ex(len(t_), 120) * .3
def clap():
    t_ = tt(.25); n0 = bp(noise(.25), 900, 4000); e = np.zeros(len(t_))
    for o in (0, .011, .022): k = int(o * SR); e[k:] += ex(len(t_) - k, 32)
    return n0 * e * .7
def hat(open_=False):
    n = int((.22 if open_ else .05) * SR); return hp(rng.standard_normal(n), 7500) * ex(n, 9 if open_ else 60)
def bass(midi, d):
    t_ = tt(d); f = 440 * 2 ** ((midi - 69) / 12); ph = np.cumsum(np.full(len(t_), f)) / SR
    saw = 2 * (ph % 1) - 1
    cut = 500 + 1800 * ex(len(t_), 14)
    y = np.zeros(len(t_)); st = 0.0; a_ = 2 * np.pi / SR
    for k_ in range(len(t_)):
        st += (saw[k_] - st) * min(1, a_ * cut[k_]); y[k_] = st
    return y * np.minimum(1, (d - t_) / .02) * np.minimum(1, t_ / .004)
PROG = [(m('A2'), (m('A4'), m('C5'), m('E5'))), (m('D2') + 12, (m('A4'), m('D5'), m('F5'))),
        (m('G2'), (m('B4'), m('D5'), m('G5'))), (m('E2') + 12, (m('G#4'), m('B4'), m('E5')))]
t = D0; beat = 0
while t < 46.6 - 1e-6:
    bar = beat // 4; root, chord = PROG[bar % 4]
    add(t, kick(), .9, 0, MUS)
    if beat % 2 == 1: add(t, clap(), .55, .05, MUS)
    for s16 in range(4):
        tt16 = t + s16 * B4 / 4
        add(tt16, hat(open_=(s16 == 2)), .2 if s16 == 2 else .1, .35, MUS)
    for h, oct_ in ((0, 0), (.5, 12)):                            # the octave bass
        add(t + h * B4, bass(root + oct_ - 12, B4 * .42), .38, -.1, MUS)
    for n_ in chord:                                              # string stabs on the off-beat
        play(VLN, SPC[VLN], n_, t + B4 / 2, .15, .34, .3)
    play(VLA, SPC[VLA], chord[0] - 12, t + B4 / 2, .15, .28, .1)
    t += B4; beat += 1
for tm in (D0, D0 + 8 * B4):                                       # the string swoops
    for i, n_ in enumerate((m('A4'), m('B4'), m('C5'), m('D5'), m('E5'), m('F5'), m('G5'), m('A5'))):
        play(VLN, SPC[VLN], n_, tm - .3 + i * .036, .12, .3, .4)
play(VLN, SUS[VLN], m('A5'), D0, 5.4, .18, .5, att=.3)
play(VLN, SUS[VLN], m('E5'), D0, 5.4, .16, .4, att=.3)
play(CEL, PZZ[CEL], m('A2'), 46.6, .6, .5, -.2)

# ════════════════════════════════════════════════════════════════════════════
# THE LOGO DITTY — 47.9–50.5, in A major: bubble bloops, pizzicato and harp, a bright landing
# ════════════════════════════════════════════════════════════════════════════
def bloop(midi, d=.16, up=True):
    t_ = tt(d); f0 = 440 * 2 ** ((midi - 69) / 12)
    f = f0 * (1 + (.6 if up else -.3) * (t_ / d) ** .7)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.sin(np.pi * t_ / d) ** .8
DIT = [(47.90, m('A4')), (48.10, m('C#5')), (48.65, m('E5')), (48.74, m('F#5')), (48.83, m('G#5')), (48.92, m('A5'))]
for tm, n_ in DIT:
    add(tm, bloop(n_ + 12, .14), .22, rng.uniform(-.3, .3), MUS)
    play(VLN, PZZ[VLN], n_, tm, .3, .45, .25)
    if tm in (47.90, 48.10): play(CEL, PZZ[CEL], n_ - 24, tm, .4, .45, -.25)
t_ = tt(1.0); f = 440 * 2 ** ((m('E5') - 69) / 12) * (1 + .5 * np.sin(np.pi * t_ / 1.0 * .5)) * (1 + .02 * np.sin(2 * np.pi * 6 * t_))
add(48.95, np.sin(2 * np.pi * np.cumsum(f) / SR) * np.sin(np.pi * t_ / 1.0) ** 1.4, .12, -.2, MUS)   # the ghost floats in
LAND = 49.95
for (ins, n_, g_) in ((CB, m('A2') - 12, .45), (CEL, m('A2'), .5), (VLA, m('C#4'), .4), (VLA, m('E4'), .36), (VLN, m('A4'), .42), (VLN, m('C#5'), .36), (VLN, m('E5'), .32)):
    play(ins, PZZ[ins], n_, LAND, .5, g_ * 1.4, 0)
    play(ins, SUS[ins], n_, LAND + .02, 1.8, g_ * .55, 0, att=.15, rel=.6)
for i, n_ in enumerate((m('A4'), m('C#5'), m('E5'), m('A5'), m('C#6'), m('E6'))):
    harp(n_, LAND + .03 + i * .05, .5, -.3 + i * .12)
add(LAND + .02, bloop(m('A6'), .22), .2, .2, MUS)
add(50.3, bloop(m('E6'), .12), .16, -.2, MUS); add(50.42, bloop(m('A6'), .14), .18, .2, MUS)   # and a wink for the tagline

# ════════════════════════════════════════════════════════════════════════════
# EFFECTS
# ════════════════════════════════════════════════════════════════════════════
UW = 2400   # most things are heard through water
def wet_lp(x, f=UW): return lp(x, f)
# the sub: engine hum, the propeller's thrum, closest around 6–12 s
t_ = tt(17.0)
hum = sum(np.sin(2 * np.pi * f * t_ + rng.uniform(0, 6)) / k for k, f in enumerate((46, 92, 138, 184, 276), 1))
prop = bp(noise(17.0), 120, 700) * (.6 + .4 * np.sin(2 * np.pi * 7.5 * t_) ** 2)
lvl = np.clip(np.minimum(t_ / 4.0, 1) * np.minimum((17.0 - t_) / 3.0, 1), 0, 1) * (.55 + .45 * np.exp(-((t_ - 9) / 4.5) ** 2))
add(0, (hum * .5 + prop) * lvl, .22, 0)
# the wake: a roar of bubbles behind the sub, with bloops in it
wk = bp(noise(13.0), 200, 1600) * np.minimum(tt(13.0) / 2, 1) * np.minimum((13.0 - tt(13.0)) / 2.5, 1)
add(4.0, wk, .16, .1)
def bub(f0, d=.05):
    t_ = tt(d); return np.sin(2 * np.pi * np.cumsum(f0 * (1 + 1.5 * t_ / d)) / SR) * np.sin(np.pi * t_ / d)
for _ in range(240):
    add(rng.uniform(4, 16.5), bub(rng.uniform(300, 1100), rng.uniform(.03, .07)), .05 * rng.uniform(.3, 1), rng.uniform(-.6, .6))
# sonar, eerie and far
for tm in (1.2, 6.8, 12.5):
    t_ = tt(2.4); add(tm, np.sin(2 * np.pi * 1180 * t_) * ex(len(t_), 2.6) * np.minimum(1, t_ / .01), .06, -.3)
# the hatch, the flashlight
t_ = tt(.6); add(14.3, (np.sin(2 * np.pi * np.cumsum(140 * np.exp(-t_ * 4) + 60) / SR) + bp(noise(.6), 300, 2000) * .4) * ex(len(t_), 6), .3, -.1)
n = int(.03 * SR); add(16.25, hp(noise(.03), 1500) * ex(n, 200), .12, .2)
# regulator breathing: inhale hiss, exhale bubble burst (14.6 to the wipe; quicker in the dance)
def inhale(d=1.0):
    t_ = tt(d); x = bp(noise(d), 900, 5200) * np.sin(np.pi * t_ / d) ** .8
    x += np.sin(2 * np.pi * np.cumsum(1700 + 300 * t_) / SR) * .04 * np.sin(np.pi * t_ / d)   # the valve's whistle
    return x
def exhale(d=1.4):
    t_ = tt(d); x = lp(noise(d), 700) * np.sin(np.pi * t_ / d) ** .6 * .7
    for _ in range(40):
        p0 = rng.uniform(0, d - .06); b_ = bub(rng.uniform(250, 900), rng.uniform(.03, .06))
        i0 = int(p0 * SR); x[i0:i0 + len(b_)] += b_ * rng.uniform(.3, .9)
    return x
t = 14.8
while t < 46.4:
    fast = t > 41.2
    add(t, inhale(.8 if fast else 1.05), .09, .05)
    add(t + (.9 if fast else 1.25), exhale(1.0 if fast else 1.45), .13, -.05)
    t += 2.6 if fast else 3.7
# footsteps in sand on the walk, muffled
for k in range(9):
    tm = 17.5 + k * .62; t_ = tt(.18)
    add(tm, wet_lp(np.sin(2 * np.pi * np.cumsum(90 * np.exp(-t_ * 18) + 40) / SR) * ex(len(t_), 25) + bp(noise(.18), 300, 1500) * ex(len(t_), 30) * .5, 900), .25, -.1)
# bubbles rising by the wreck
for _ in range(90):
    add(rng.uniform(26.4, 29.6), bub(rng.uniform(400, 1300), rng.uniform(.03, .06)), .05 * rng.uniform(.3, 1), rng.uniform(-.5, .5))
# the tablet: a muffled tap, a scanning hum, the finding chime
n = int(.03 * SR); add(34.55, wet_lp(hp(noise(.03), 1500) * ex(n, 200), 3000), .18, .2)
t_ = tt(.3); add(34.6, np.sin(2 * np.pi * np.cumsum(700 + 600 * t_ / .3) / SR) * np.sin(np.pi * t_ / .3), .05, .2)
t_ = tt(4.9); add(34.7, bp(noise(4.9), 1800, 2600) * .3 * np.minimum(1, t_ / .5) * np.minimum(1, (4.9 - t_) / .3), .03, .2)
for k, f in enumerate((1318.5, 1760.0, 2217.5)):
    t_ = tt(.5); add(39.7 + k * .08, wet_lp(np.sin(2 * np.pi * f * t_) * ex(len(t_), 7), 3200), .06, .2)
# the dance: bursts of bubbles with the moves
for tm in (42.0, 43.0, 44.0, 45.0, 46.0):
    for _ in range(18): add(tm + rng.uniform(0, .4), bub(rng.uniform(400, 1200), rng.uniform(.03, .06)), .05, rng.uniform(-.5, .5))
# the wipe: a great rush of bubbles
t_ = tt(1.5); add(46.45, bp(noise(1.5), 200, 2500) * np.sin(np.pi * t_ / 1.5) ** 1.2, .4, 0)
for _ in range(260):
    add(rng.uniform(46.5, 47.9), bub(rng.uniform(300, 1500), rng.uniform(.02, .06)), .06 * rng.uniform(.3, 1), rng.uniform(-.8, .8))
for _ in range(30):
    add(rng.uniform(48.0, 52.0), bub(rng.uniform(500, 1300), rng.uniform(.03, .05)), .02, rng.uniform(-.6, .6))

sc.mix(OUT, hall=.36, room=.24)
x = wavfile.read(OUT)[1].astype(float) / 32767
print('ok', [round(float(np.sqrt((x[int(s * SR):int((s + 2.5) * SR)] ** 2).mean())), 3) for s in np.arange(0, 52.5, 2.5)])
