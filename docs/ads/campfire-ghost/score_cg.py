#!/usr/bin/env python3
"""
Sound for the campfire ghost ad (47.7 s), on the IsHaunted strings kit.
Cut points (seconds, 24 fps):
  0–6.6   the campfire pan            6.6–12.2 the kid stares at the tablet      12.2 the tablet turns
  12.2–18.3 push in on the still bedroom        18.7–22.9 the havoc, on the tablet's little speaker
  23.0 the ghost forms, 23.33 breaks the screen, 24.0–24.63 rushes the lens, the flash
  24.67 the night bedroom (ghost lands 25.2), 26.95 the chase, 29.7 the swoosh, 30.1 the door
  30.38 pull back into the IsHaunted video editor: 32.3 grab the playhead, 33.0 let go, 33.46 Marker,
        34.29 Callout, 34.7–35.1 the box, 35.2 "GHOST?!"
  36.1 the editor flies onto the kid's tablet, 36.8–39.75 the campfire rewinds to the kid
  39.8–42.3 he melts (the ghost rises at 41.3)   42.3–43.0 the rush to the dark   43.04 the logo build
  44.1–45.1 the logo's ghost floats in, 45.0 the wordmark lands, 45.4 tagline, 46.35 subline, 47.7 end
Music: eerie (E minor, the half-step and the tritone) -> chase spiccato -> sly pizzicato for the editor ->
a sad-trombone melt in the cellos -> the logo ditty in E major.
Effects are designed, not sampled.
    python3 score_cg.py out.wav
"""
import os, sys, math, random
KIT = os.environ.get('STRINGS_KIT', os.path.expanduser('~/Music/IsHaunted Strings')); sys.path.insert(0, KIT)
import numpy as np
from scipy.signal import fftconvolve
from strings import *

DUR = 1145 / 24
sc = Score(DUR); harp, place = sc.harp, sc.place
def play(inst, art, midi, t0, dur, vel=.7, pan=0.0, bus=None, rel=.35, att=0.0):
    sc.play(inst, art, midi, t0, dur, vel, pan, bus, rel, min(att, .9 * (dur + rel)))   # an attack can't outlast the note
MUS, SFX, N = sc.mus, sc.sfx, sc.n
rng = np.random.default_rng(31); random.seed(31)
def noise(d): return rng.standard_normal(max(1, int(d * SR)))
def ex(n, k): return np.exp(-np.arange(n) / SR * k)
def add(t0, x, g=1.0, pan=0.0, bus=None): place(SFX if bus is None else bus, t0, x, g, pan)
def sweep(d, f0, f1, shape=1.0):
    t_ = tt(d); f = f0 + (f1 - f0) * (t_ / d) ** shape
    return np.sin(2 * np.pi * np.cumsum(f) / SR)
def whoosh(d, lo=250, hi=3500, peak=.6, rise=1.6):
    t_ = tt(d); env = np.where(t_ < peak * d, (t_ / (peak * d)) ** rise, ((d - t_) / ((1 - peak) * d + 1e-9)) ** 1.3)
    x = noise(d); y = np.zeros_like(x); nseg = 24; L = len(x) // nseg + 1
    for i in range(nseg):                                     # a moving band: the air going past
        a, b = i * L, min(len(x), (i + 1) * L); c = i / nseg
        f1 = lo + (hi - lo) * math.sin(math.pi * min(1, c / max(peak, .05)) / 2) ** 2
        y[a:b] = bp(x[max(0, a - 2000):b], max(60, f1 * .5), min(16000, f1 * 1.6))[-(b - a):]
    return y * env
def boom(d=1.6, f0=70, f1=32):
    t_ = tt(d); return (sweep(d, f0, f1, .5) * ex(len(t_), 2.6) + lp(noise(d), 160) * ex(len(t_), 4) * .8)
def thud(d=.35, f=90):
    t_ = tt(d); return sweep(d, f * 1.6, f, .3) * ex(len(t_), 14) + bp(noise(d), 200, 1500) * ex(len(t_), 30) * .6
def crash(d=1.0, bright=1.0):
    t_ = tt(d); return (hp(noise(d), 1800) * ex(len(t_), 5) * bright + bp(noise(d), 300, 2500) * ex(len(t_), 9) + thud(d, 70) * .8)
def glass(d=1.4):
    out = hp(noise(d), 3000) * ex(int(d * SR), 6) * .8
    for k in range(26):
        f = rng.uniform(2500, 9000); s = rng.uniform(0, d * .6); n = int(rng.uniform(.03, .2) * SR)
        g = np.zeros(int(d * SR)); i = int(s * SR); seg_ = np.sin(2 * np.pi * f * np.arange(n) / SR) * ex(n, rng.uniform(25, 60))
        g[i:i + n] += seg_[:len(g) - i]; out += g * rng.uniform(.2, .7)
    return out
def click(down=True):
    n = int(.012 * SR); x = hp(noise(.012), 2500) * ex(n, 600 if down else 900)
    return x + np.sin(2 * np.pi * (2400 if down else 3100) * np.arange(n) / SR) * ex(n, 500) * .4
def blip(f=1760, d=.18):
    t_ = tt(d); return (np.sin(2 * np.pi * f * t_) + .3 * np.sin(4 * np.pi * f * t_)) * ex(len(t_), 18)
def speaker(x):                                               # what a tablet's little speaker does to it
    y = bp(x, 380, 5200, 3); return np.tanh(y * 2.2) / 2.2
def shatter(d=1.2, size=1.0):                                   # china, a lamp base: ringing shards
    n = int(d * SR); out = np.zeros(n)
    for k in range(int(40 * size)):
        f = rng.uniform(1800, 7500); s0 = rng.uniform(0, d * .35) ** 1.5; m_ = int(rng.uniform(.04, .25) * SR); i = int(s0 * SR)
        seg_ = (np.sin(2 * np.pi * f * np.arange(m_) / SR) + .5 * np.sin(2 * np.pi * f * 2.76 * np.arange(m_) / SR)) * ex(m_, rng.uniform(18, 45))
        out[i:i + m_] += seg_[:n - i] * rng.uniform(.2, .8)
    out += hp(noise(d), 2500) * ex(n, 9) * .9 + bp(noise(d), 400, 2500) * ex(n, 22) * .8
    return out + thud(d, 85)[:n] * .9
def bookfall(t0, k=7, g=.4, pan=0.0, fn=None):
    fn = fn or add
    for j in range(k):
        tm = t0 + j * rng.uniform(.04, .09); fn(tm, thud(.22, rng.uniform(150, 260)) + bp(noise(.22), 900, 4000) * ex(int(.22 * SR), 40) * .5, g * rng.uniform(.6, 1), pan + rng.uniform(-.3, .3))
TAB = np.zeros((N, 2))                                       # the havoc on the tablet, filtered at the end
def tab(t0, x, g=1.0, pan=.15): place(TAB, t0, x, g, pan)

# ════════════════════════════════════════════════════════════════════════════
# A · 0–6.6 the campfire: a warm low E with something not right in it
# ════════════════════════════════════════════════════════════════════════════
play(CB, SUS[CB], m('E2') - 12, 0.0, 12.4, .5, -.1, att=2.5)
play(CEL, SUS[CEL], m('E2'), 0.3, 12.1, .42, -.25, att=3.0)
play(VLA, SUS[VLA], m('B3'), 1.2, 5.6, .26, .2, att=2.0)
for i, n_ in enumerate((m('E3'), m('B3'), m('G4'), m('B3'), m('E3'), m('B3'), m('F#4'), m('B3'))):   # a campfire-story harp
    harp(n_, .6 + i * .72, .34, -.3 + (i % 4) * .15)
play(VLN, TRM[VLN], m('B5'), 3.6, 4.0, .11, .45, att=2.5)
play(VLN, TRM[VLN], m('C6'), 4.6, 3.0, .09, .55, att=2.5)       # the minor second: unease
# ════════════════════════════════════════════════════════════════════════════
# B · 6.6–12.2 the kid stares at the tablet: a heartbeat, the violins creeping up
# ════════════════════════════════════════════════════════════════════════════
play(VLA, TRM[VLA], m('A#3'), 6.8, 5.2, .22, .1, att=2.5)       # the tritone
t = 7.6; gap = 1.0
while t < 12.0:
    play(CB, PZZ[CB], m('E2') - 12, t, .5, .5, -.1); play(CB, PZZ[CB], m('E2') - 12, t + .26, .4, .32, -.1)
    t += gap; gap = max(.62, gap * .93)
for tm, n_ in ((7.4, m('B5')), (8.8, m('C6')), (10.0, m('C#6')), (11.0, m('D6'))):
    play(VLN, TRM[VLN], n_, tm, 1.6, .12 + (tm - 7) * .02, .45, att=.8)
play(CEL, TRM[CEL], m('E2'), 9.6, 2.7, .3, -.3, att=2.2)
# ════════════════════════════════════════════════════════════════════════════
# C · 12.2 the tablet turns: a sting; 12.3–18.5 the still bedroom, a music-box lullaby that goes wrong
# ════════════════════════════════════════════════════════════════════════════
for ins, n_, g_ in ((CB, m('E2') - 12, .6), (CEL, m('F2'), .5), (VLA, m('A#3'), .45), (VLN, m('E5'), .4), (VLN, m('F5'), .35)):
    play(ins, SPC[ins], n_, 12.2, .3, g_, 0)
    play(ins, TRM[ins], n_, 12.22, 1.4, g_ * .45, 0, att=.05, rel=.9)
for i, n_ in enumerate((m('F6'), m('E6'), m('C6'), m('B5'), m('A#5'), m('F5'), m('E5'), m('B4'))):
    harp(n_, 12.2 + i * .05, .4, .4 - i * .1)
LUL = [m('E5'), m('G5'), m('B5'), m('A5'), m('G5'), m('F#5'), m('E5'), m('B4'), m('E5'), m('G5'), m('B5'), m('C6'), m('B5'), m('A#5'), m('E5'), m('F5')]
for i, n_ in enumerate(LUL):                                    # each round a little more out of tune
    tm = 13.0 + i * .34
    harp(n_ + (0 if i < 11 else rng.choice([0, -1, 1])), tm, .24 + i * .006, .3)
play(VLN, SUS[VLN], m('E6'), 13.0, 5.3, .07, .55, att=3.0)
play(CEL, SUS[CEL], m('E2'), 13.0, 5.5, .2, -.3, att=2.0)
for i, (n_, ins) in enumerate(((m('E3'), CEL), (m('F3'), CEL), (m('A#3'), VLA), (m('B3'), VLA), (m('E4'), VLN), (m('F4'), VLN), (m('A#4'), VLN))):   # the swell
    play(ins, TRM[ins], n_, 15.6 + i * .25, 2.95 - i * .25, .18 + i * .03, -.3 + i * .1, att=1.6 - i * .15, rel=.05)
# 18.55: everything cuts out -- one breath of silence -- then the havoc

# ════════════════════════════════════════════════════════════════════════════
# D · 18.7–22.9 the havoc (on the tablet): spiccato ostinato at 150, stabs on the hits
# ════════════════════════════════════════════════════════════════════════════
E8 = 60 / 150 / 2
OST = [m('E3'), m('E3'), m('G3'), m('E3'), m('A#3'), m('E3'), m('A3'), m('G3')]
t = 18.72; k = 0
while t < 22.95:
    n_ = OST[k % 8] + (12 if (k // 16) % 2 else 0) * 0
    play(CEL, SPC[CEL], n_, t, E8, .48 if k % 4 == 0 else .32, -.25)
    play(VLA, SPC[VLA], n_ + 12, t, E8, .38 if k % 4 == 0 else .24, .2)
    if k % 2 == 0: play(CB, SPC[CB], m('E2') - 12 + (5 if (k // 8) % 2 else 0), t, E8, .4, -.1)
    t += E8; k += 1
for tm in (19.7, 20.2, 20.67, 21.2, 22.0, 22.6):
    for ins, n_ in ((VLN, m('A#5')), (VLN, m('E6')), (VLA, m('E4')), (CEL, m('A#2'))):
        play(ins, SPC[ins], n_, tm, .2, .5, 0)
for i in range(8):                                              # a chromatic violin run up into the exit
    play(VLN, SPC[VLN], m('E5') + i, 22.62 + i * .045, .1, .4, .3)
# ════════════════════════════════════════════════════════════════════════════
# E · 23.0–24.63 the ghost comes out of the tablet: everything swells to the flash
# ════════════════════════════════════════════════════════════════════════════
for i, (ins, n_) in enumerate(((CB, m('E2') - 12), (CEL, m('E2')), (CEL, m('F2')), (VLA, m('A#3')), (VLA, m('B3')), (VLN, m('E5')), (VLN, m('F5')), (VLN, m('A#5')), (SVN, m('E6')))):
    play(ins, TRM[ins], n_, 23.0 + i * .06, 1.65 - i * .06, .3 + i * .05, -.4 + i * .1, att=1.4 - i * .05, rel=.03)
for ins, n_, g_ in ((CB, m('E2') - 12, .8), (CEL, m('E2'), .75), (VLA, m('B3'), .6), (VLN, m('E5'), .55), (VLN, m('B5'), .5)):
    play(ins, SPC[ins], n_, 24.63, .3, g_, 0)
    play(ins, SUS[ins], n_, 24.65, 1.4, g_ * .35, 0, att=.02, rel=1.2)
# ════════════════════════════════════════════════════════════════════════════
# F · 24.67–30.3 the night bedroom: the tail of the hit, a held breath, then the chase
# ════════════════════════════════════════════════════════════════════════════
play(VLN, TRM[VLN], m('B5'), 25.3, 1.8, .08, .5, att=1.0)
play(CEL, TRM[CEL], m('E2'), 25.4, 1.6, .18, -.3, att=1.2)
E8b = 60 / 168 / 2
t = 26.95; k = 0
while t < 30.05:
    n_ = OST[k % 8] + 1                                         # up a half-step: F minor, worse
    play(CEL, SPC[CEL], n_, t, E8b, .5 if k % 4 == 0 else .34, -.25)
    play(VLA, SPC[VLA], n_ + 12, t, E8b, .4 if k % 4 == 0 else .26, .2)
    if k % 2 == 0: play(CB, SPC[CB], m('F2') - 12, t, E8b, .45, -.1)
    t += E8b; k += 1
for tm in (27.2, 28.4, 29.7):
    for ins, n_ in ((VLN, m('B5')), (VLN, m('F6')), (VLA, m('F4')), (CEL, m('B2'))):
        play(ins, SPC[ins], n_, tm, .22, .55, 0)
for i in range(10):
    play(VLN, SPC[VLN], m('F5') + i, 29.2 + i * .045, .1, .38, .3)
play(VLN, TRM[VLN], m('F6'), 30.1, 1.4, .16, .4, att=.05, rel=.8)       # the door: one held note
play(CEL, SUS[CEL], m('B2'), 30.1, 1.2, .25, -.3, att=.05, rel=.8)
# ════════════════════════════════════════════════════════════════════════════
# G · 30.38–36.1 the IsHaunted video editor: sly pizzicato, every click on a note
# ════════════════════════════════════════════════════════════════════════════
BT = 60 / 112; t = 31.0; k = 0
WALK = [m('E2'), m('G2'), m('A2'), m('A#2'), m('B2'), m('A#2'), m('A2'), m('G2')]
while t < 36.0:
    play(CB, PZZ[CB], WALK[k % 8], t, .35, .5, -.15)
    if k % 2 == 1: play(VLA, PZZ[VLA], WALK[k % 8] + 19, t, .25, .3, .2)
    t += BT / 2 if k % 4 in (1, 2) else BT * .75; k += 1
for tm, n_ in ((32.3, m('B4')), (33.0, m('E5'))):                 # grab and let go
    play(VLN, PZZ[VLN], n_, tm, .2, .4, .3)
for i in range(6):                                              # the scrub back: a pizzicato run down
    play(VLN, PZZ[VLN], m('E6') - i * 2, 32.35 + i * .1, .12, .3, .35)
harp(m('E6'), 33.58, .55, .3); harp(m('B5'), 33.66, .45, .25)       # the marker pops in
for i, n_ in enumerate((m('E4'), m('G4'), m('B4'), m('D5'), m('E5'))):   # the callout box grows
    play(VLN, PZZ[VLN], n_, 34.7 + i * .09, .15, .35, .3)
play(CB, PZZ[CB], m('E2') - 12, 35.2, .6, .7, 0); play(CB, PZZ[CB], m('D#2') - 12, 35.42, .9, .6, 0)   # "GHOST?!" -- bwomp
play(VLN, TRM[VLN], m('A#5'), 35.25, .9, .12, .45, att=.1)
# ════════════════════════════════════════════════════════════════════════════
# H · 36.1–39.75 onto the tablet; the campfire rewinds -- a reversed swell, then quiet
# ════════════════════════════════════════════════════════════════════════════
for i, (ins, n_) in enumerate(((CEL, m('E2')), (VLA, m('B3')), (VLN, m('E5')), (VLN, m('G5')))):
    play(ins, TRM[ins], n_, 36.5, 2.7, .22 + i * .03, -.3 + i * .2, att=2.6, rel=.04)   # swells and stops dead: a rewind
play(CB, SUS[CB], m('E2') - 12, 39.2, 1.2, .3, -.1, att=.4)
play(VLN, TRM[VLN], m('B5'), 39.25, .7, .1, .45, att=.3)
# ════════════════════════════════════════════════════════════════════════════
# I · 39.8–42.3 the kid melts: a sad trombone in the cellos, and the ghost rising in the harp
# ════════════════════════════════════════════════════════════════════════════
for i, n_ in enumerate((m('E3'), m('D#3'), m('D3'))):
    play(CEL, SUS[CEL], n_, 40.0 + i * .42, .44, .62, -.1, att=.03, rel=.1)
    play(CB, SUS[CB], n_ - 12, 40.0 + i * .42, .44, .45, -.1, att=.03, rel=.1)
for j in range(10):                                             # ...the last one droops and wobbles down
    play(CEL, SUS[CEL], m('C#3') - j * .3, 41.26 + j * .09, .12, .6 * (1 - j * .05), -.1, att=.01, rel=.06)
play(CB, SUS[CB], m('C#2') - 12, 41.26, 1.0, .4, -.1, att=.02, rel=.4)
for i, n_ in enumerate((m('E4'), m('G4'), m('B4'), m('D5'), m('E5'), m('G5'), m('B5'), m('D6'), m('E6'))):
    harp(n_, 41.4 + i * .07, .32, -.3 + i * .07)
play(VLN, TRM[VLN], m('E6'), 41.5, .9, .1, .5, att=.5)
# ════════════════════════════════════════════════════════════════════════════
# J · 42.3–43.04 the rush, and the dark
# ════════════════════════════════════════════════════════════════════════════
for i, (ins, n_) in enumerate(((CEL, m('E2')), (CEL, m('B2')), (VLA, m('E4')), (VLN, m('B4')), (VLN, m('E5')), (VLN, m('B5')))):
    play(ins, TRM[ins], n_, 42.3 + i * .04, .74 - i * .04, .3 + i * .06, -.3 + i * .12, att=.6, rel=.02)
# ════════════════════════════════════════════════════════════════════════════
# K · 43.04–47.7 THE LOGO DITTY, in E major: pizzicato and harp on the pieces, the ghost floats in, a bright landing
# ════════════════════════════════════════════════════════════════════════════
L0 = 43.04
DIT = [(L0, m('E4')), (L0 + .2, m('G#4')), (L0 + .76, m('B4')), (L0 + .85, m('C#5')), (L0 + .94, m('D#5')), (L0 + 1.03, m('E5'))]
for tm, n_ in DIT:
    play(VLN, PZZ[VLN], n_, tm, .3, .45, .25)
    if tm in (L0, L0 + .2): play(CEL, PZZ[CEL], n_ - 24, tm, .4, .5, -.25)
t_ = tt(1.0); f = 440 * 2 ** ((m('B5') - 69) / 12) * (1 + .5 * np.sin(np.pi * t_ / 1.0 * .5)) * (1 + .02 * np.sin(2 * np.pi * 6 * t_))
add(L0 + 1.06, np.sin(2 * np.pi * np.cumsum(f) / SR) * np.sin(np.pi * t_ / 1.0) ** 1.4, .1, -.2, MUS)   # the ghost floats in
play(SVN, SUS[SVN], m('B5'), L0 + 1.1, .9, .2, -.2, att=.4, rel=.3)
LAND = L0 + 1.95
for (ins, n_, g_) in ((CB, m('E2') - 12, .45), (CEL, m('E2'), .5), (VLA, m('G#3'), .4), (VLA, m('B3'), .36), (VLN, m('E4'), .42), (VLN, m('G#4'), .36), (VLN, m('B4'), .32), (VLN, m('E5'), .3)):
    play(ins, PZZ[ins], n_, LAND, .5, g_ * 1.4, 0)
    play(ins, SUS[ins], n_, LAND + .02, 2.0, g_ * .55, 0, att=.15, rel=.7)
for i, n_ in enumerate((m('E4'), m('G#4'), m('B4'), m('E5'), m('G#5'), m('B5'), m('E6'))):
    harp(n_, LAND + .03 + i * .05, .5, -.3 + i * .1)
play(VLN, PZZ[VLN], m('B5'), 46.0, .2, .4, .2); play(VLN, PZZ[VLN], m('E6'), 46.12, .3, .45, .25)   # a wink for the tagline
harp(m('G#6'), 46.85, .3, .3)

# ════════════════════════════════════════════════════════════════════════════
# EFFECTS
# ════════════════════════════════════════════════════════════════════════════
# the campfire: a crackling fire (louder while it's in the shot) and crickets, 0–24.6, and back for the rewind
def fire(d, level):
    x = lp(noise(d), 700) * .25
    for k in range(int(d * 18)):
        p = rng.uniform(0, d - .02); n = int(rng.uniform(.001, .006) * SR); i = int(p * SR)
        x[i:i + n] += hp(rng.standard_normal(n), 1200)[:len(x[i:i + n])] * rng.uniform(.3, 1.4) * ex(n, 400)[:len(x[i:i + n])]
    for k in range(int(d * 1.2)):                               # the odd big pop
        p = rng.uniform(0, d - .05); n = int(.04 * SR); i = int(p * SR); x[i:i + n] += bp(noise(.04), 800, 6000)[:len(x[i:i + n])] * 2 * ex(n, 120)[:len(x[i:i + n])]
    t_ = tt(d); return x * level(t_)
add(0.0, fire(18.6, lambda t_: np.interp(t_, [0, 5, 7, 12, 18.6], [1, 1, .55, .4, .3])), .14, -.2)
add(36.6, fire(3.3, lambda t_: np.interp(t_, [0, .5, 3.3], [0, .4, .45])), .12, -.2)
def crickets(d, seed):
    r = np.random.default_rng(seed); out = np.zeros(int(d * SR)); t0 = 0
    f = r.uniform(4200, 5200)
    while t0 < d - .3:
        for c in range(3):                                      # chirp-chirp-chirp
            n = int(.045 * SR); i = int((t0 + c * .07) * SR); s_ = np.sin(2 * np.pi * f * np.arange(n) / SR) * (1 + np.sin(2 * np.pi * 70 * np.arange(n) / SR)) * np.sin(np.pi * np.arange(n) / n)
            out[i:i + n] += s_[:len(out[i:i + n])]
        t0 += r.uniform(.55, 1.1)
    return out
for sd, pn, g_ in ((1, -.7, .04), (2, .6, .03), (3, .1, .02)):
    add(0.0, crickets(24.6, sd), g_, pn); add(36.6, crickets(3.4, sd + 10), g_ * .8, pn)
add(12.15, whoosh(.5, 400, 3000, .5), .12, -.3)                  # the tablet turns: a swish of a sleeve
# the forest around the campfire: wind in the trees, leaves, an owl, a twig, a far-off frog
def forest(d, seed):
    r = np.random.default_rng(seed); n = int(d * SR); t_ = np.arange(n) / SR
    gust = .55 + .45 * np.sin(2 * np.pi * .07 * t_ + 1) * np.sin(2 * np.pi * .023 * t_ + 2) + .15 * np.sin(2 * np.pi * .31 * t_)
    wind = lp(r.standard_normal(n), 380) * 1.6 + bp(r.standard_normal(n), 300, 1400) * .5
    leaves = bp(r.standard_normal(n), 1800, 7000) * (np.clip(gust - .5, 0, 1) * 2) ** 1.5 * (.6 + .4 * np.abs(np.sin(2 * np.pi * 3.1 * t_)))
    return wind * gust * .5, leaves * .5
for (t0, d, sd, g_) in ((0.0, 24.65, 7, 1.0), (36.6, 5.7, 8, .8)):
    w_, l_ = forest(d, sd)
    add(t0, np.stack([w_, np.roll(w_, 900)], 1), .32 * g_, 0)
    add(t0, np.stack([np.roll(l_, 400), l_], 1), .2 * g_, 0)
def owl(t0, pan):
    for k, (dt, d, f) in enumerate(((0, .32, 410), (.5, .18, 390), (.72, .5, 380))):
        t_ = tt(d); x = np.sin(2 * np.pi * np.cumsum(f * (1 + .04 * np.sin(np.pi * t_ / d)) + 3 * np.sin(2 * np.pi * 6 * t_)) / SR)
        x = x * np.sin(np.pi * t_ / d) ** 1.6 + bp(noise(d), 300, 900) * .15 * np.sin(np.pi * t_ / d)
        add(t0 + dt, lp(x, 1400), .05, pan)
owl(2.4, -.7); owl(9.8, .75); owl(15.9, -.6)
for tm, pn in ((5.6, .6), (13.4, -.5), (37.8, .5)):           # a twig snaps somewhere out in the dark
    n = int(.05 * SR); add(tm, (hp(noise(.05), 1500) * ex(n, 90) + bp(noise(.05), 300, 2000) * ex(n, 60)), .07, pn)
for k in range(12):                                            # a frog, far off
    tm = 1.0 + k * 1.9 + rng.uniform(0, .5)
    if tm > 24: break
    t_ = tt(.09); add(tm, lp(np.sign(np.sin(2 * np.pi * 180 * t_)) * np.sin(np.pi * t_ / .09), 900), .012, .8)
# the kid gasps (8.36): a sharp breath in, a child's voice catching on it
def gasp():
    d = .55; t_ = tt(d); n = len(t_)
    env = np.where(t_ < .05, (t_ / .05) ** 1.2, np.exp(-(t_ - .05) * 5.5)) * (1 + .25 * np.exp(-((t_ - .18) / .05) ** 2))
    br = noise(d); out = np.zeros(n)
    for f, q, g_ in ((950, 6, 1.0), (1650, 7, .8), (2900, 8, .55), (4200, 6, .35)):   # an "ah" shape, breathed in
        out += bp(br, f * (1 - .5 / q), f * (1 + .5 / q)) * g_
    out += hp(br, 5000) * .25
    f0 = 330 + 60 * np.exp(-t_ * 30); voice = np.sin(2 * np.pi * np.cumsum(f0 * (1 + .01 * rng.standard_normal(n))) / SR)
    voice = bp(voice + .4 * np.sign(voice) * .2, 600, 2500) * np.exp(-((t_ - .045) / .03) ** 2) * .5   # the catch in the throat
    return (out * env + voice) * 1.0
add(8.36, gasp(), .8, .25)
# the havoc, on the tablet's speaker
tab(18.72, whoosh(.6, 200, 2500, .5), .5)                         # the blanket whips off the bed
tab(19.05, whoosh(.5, 300, 4000, .45), .5)                        # across the floor
tab(19.74, whoosh(.25, 600, 5000, .4), .45); tab(19.78, shatter(1.3, 1.0), .75)   # the lamp goes over and breaks
tab(19.8, thud(.3, 120), .5)
bookfall(19.98, 9, .55, .1, tab)                                   # the stack of books comes down
tab(20.62, whoosh(.5, 250, 3000, .4), .5)
tab(20.98, whoosh(.35, 400, 4000, .5), .45); tab(21.44, thud(.3, 95), .55); tab(21.6, thud(.25, 110), .45)   # the cushions fly and hit the wall
tab(21.98, whoosh(.45, 500, 6000, .5), .55)
tab(22.5, whoosh(.2, 800, 6000, .5), .4); tab(22.54, glass(1.5), .85); tab(22.54, thud(.35, 100), .7)   # the picture frame: glass explodes
tab(22.76, thud(.25, 180), .45); tab(22.86, thud(.2, 220), .35)    # ...and clatters on the floor
TAB = np.stack([speaker(TAB[:, c]) for c in (0, 1)], 1) * .55 + TAB * .5   # the tablet's speaker, but with weight to it
SFX += TAB
# the ghost leaves the tablet
t_ = tt(1.65); add(23.0, lp(noise(1.65), 160) * (t_ / 1.65) ** 1.4, .9, 0)          # rumble builds
add(23.28, hp(noise(.5), 3500) * ex(int(.5 * SR), 7) + sweep(.5, 1200, 3800) * ex(int(.5 * SR), 9) * .4, .22, .15)   # the screen ripples
add(23.85, whoosh(.8, 150, 5000, .92, 2.2), .6, 0)
add(24.62, boom(1.8), .8, 0); add(24.62, hp(noise(.6), 2000) * ex(int(.6 * SR), 6), .3, 0)
# the night bedroom
add(24.7, whoosh(.6, 3000, 300, .15), .3, .2); add(25.15, thud(.4, 60), .35, -.1)  # the ghost lands
t_ = tt(5.6); add(24.7, bp(noise(5.6), 120, 600) * .5, .06, 0)                    # room tone
for tm, d, pn in ((26.95, .45, -.4), (27.2, .35, .3), (28.4, .4, -.2), (29.68, .45, .5)):
    add(tm, whoosh(d, 300, 4500, .5), .45, pn)
for k in range(30):                                              # papers, things on the floor
    tm = rng.uniform(27.4, 29.6); n = int(.08 * SR); add(tm, bp(noise(.08), 1500, 7000) * ex(n, 30), .08, rng.uniform(-.7, .7))
add(27.72, thud(.3, 140), .4, -.4); add(27.78, shatter(.9, .6), .45, -.4)   # the bedside lamp gets knocked off
add(28.4, crash(.8, .6), .5, -.2)                                         # something hits the wall
add(28.94, whoosh(.25, 500, 5000, .4), .35, .3); bookfall(28.98, 10, .55, .35)  # the books swept off the dresser
add(29.3, thud(.3, 120), .4, .2)
def creak(d):
    t_ = tt(d); rate = 18 + 22 * np.sin(np.pi * t_ / d) + 6 * np.sin(2 * np.pi * 1.7 * t_)
    ph = np.cumsum(rate) / SR; imp = (np.diff(np.floor(ph), prepend=0) > 0).astype(float) * rng.uniform(.5, 1, len(t_))
    out = np.zeros(len(t_))
    for f, q in ((430, .12), (910, .07), (1580, .05), (2600, .03)):
        k_ = np.exp(-np.arange(int(.03 * SR)) / SR * 2 * np.pi * f * q) * np.sin(2 * np.pi * f * np.arange(int(.03 * SR)) / SR)
        out += fftconvolve(imp, k_)[:len(t_)]
    return out * np.sin(np.pi * t_ / d) ** .5
add(30.0, creak(1.0), .2, .3); add(30.15, lp(whoosh(.5, 200, 1500, .4), 900), .4, .4)   # through the dark door
# the editor: the room goes quiet, the mouse does the talking
add(30.4, whoosh(.6, 2500, 400, .2), .12, 0)                    # the pull back
add(32.3, click(True), .5, .2); add(33.0, click(False), .45, .2)
t_ = tt(.7); add(32.3, bp(noise(.7), 900, 3000) * (1 + np.sin(2 * np.pi * 26 * t_)) * .5 * np.sin(np.pi * t_ / .7), .05, .2)   # scrubbing
add(33.46, click(True), .5, .3); add(33.52, click(False), .4, .3); add(33.6, blip(1760), .12, .2)
add(34.29, click(True), .5, .3); add(34.35, click(False), .4, .3)
add(34.7, click(True), .45, .1); add(35.1, click(False), .4, .1)
add(35.2, blip(1318.5, .14), .12, 0); add(35.27, blip(1760, .2), .12, 0)
# onto the tablet, the rewind
add(36.12, whoosh(.65, 3500, 300, .3), .35, 0); add(36.74, thud(.2, 160), .2, 0)
t_ = tt(2.95); rate = 34 * (1 - t_ / 2.95) ** 1.5 + 3                 # a tape rewind, slowing down
ph = np.cumsum(rate) / SR
rew = bp(noise(2.95), 1200, 5000) * (.5 + .5 * np.sin(2 * np.pi * ph)) * np.minimum(1, t_ / .2) * (1 - t_ / 2.95) ** .6
rew += sweep(2.95, 3200, 900, .6) * .08 * (1 - t_ / 2.95)
add(36.8, rew, .34, 0)
# the melt: a ghostly sigh, a sizzle, gloops, the ghost lifting out
t_ = tt(2.4); vf = 300 - 120 * (t_ / 2.4)
sigh = bp(noise(2.4), 300, 1200) * .4 + np.sin(2 * np.pi * np.cumsum(vf * (1 + .03 * np.sin(2 * np.pi * 5 * t_))) / SR) * .3
add(39.85, sigh * np.sin(np.pi * t_ / 2.4) ** 1.5, .2, .1)
add(40.1, hp(noise(1.9), 4000) * np.sin(np.pi * tt(1.9) / 1.9) * .6, .07, .2)
for k in range(9):
    tm = 40.4 + k * .19 + rng.uniform(0, .06); t_ = tt(.14)
    add(tm, np.sin(2 * np.pi * np.cumsum(140 + 380 * (t_ / .14) ** 2) / SR) * np.sin(np.pi * t_ / .14), .14, rng.uniform(-.4, .5))
add(41.3, whoosh(1.0, 400, 3000, .7), .25, -.1)
# the rush and the dark
add(42.3, whoosh(.74, 150, 6000, .95, 2.4), .65, 0)
add(43.02, boom(1.8, 60, 28), .7, 0)
# the logo: whooshes for the pieces, plinks for the windows, a breath for the ghost, a ding for the tagline
for tc, pn in ((L0, -.6), (L0 + .2, .6)):
    add(tc, whoosh(.5, 300, 4000, .5), .14, pn)
for k in range(4):
    t_ = tt(.3); add(L0 + .76 + k * .09, np.sin(2 * np.pi * (2093 + k * 260) * t_) * ex(len(t_), 16), .035, -.2 + k * .13)
add(L0 + 1.05, whoosh(1.0, 500, 2500, .5), .12, -.3)
add(LAND, thud(.25, 80), .2, 0)
t_ = tt(.5); add(46.0, np.sin(2 * np.pi * 2637 * t_) * ex(len(t_), 9), .04, 0)

out = sys.argv[1] if len(sys.argv) > 1 else 'campfire_ghost_score.wav'
sc.mix(out, hall=.34, room=.2, music_peak=.85, sfx_peak=.7, fade_in=.3, fade_out=.5)
print('wrote', out)
