import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
from scipy.signal import butter, sosfilt, fftconvolve
from scipy.io import wavfile

SR = 44100
DUR = 30.0
N = int(SR * DUR)
rng = np.random.default_rng(7)
L = np.zeros(N); R = np.zeros(N)
MUS_L = np.zeros(N); MUS_R = np.zeros(N)

def tt(d): return np.arange(int(d * SR)) / SR
def midi(m): return 440.0 * 2 ** ((m - 69) / 12)
def bp(x, lo, hi, o=2): return sosfilt(butter(o, [lo, hi], 'band', fs=SR, output='sos'), x)
def lp(x, f, o=2): return sosfilt(butter(o, f, 'low', fs=SR, output='sos'), x)
def hp(x, f, o=2): return sosfilt(butter(o, f, 'high', fs=SR, output='sos'), x)

def add(t0, sig, g=1.0, pan=0.0, bus='sfx'):
    i = int(t0 * SR)
    if i >= N: return
    sig = sig[:max(0, N - i)]
    if i < 0: sig = sig[-i:]; i = 0
    l = g * np.sqrt((1 - pan) / 2); r = g * np.sqrt((1 + pan) / 2)
    if bus == 'sfx':
        L[i:i + len(sig)] += sig * l; R[i:i + len(sig)] += sig * r
    else:
        MUS_L[i:i + len(sig)] += sig * l; MUS_R[i:i + len(sig)] += sig * r

def adsr(n, a=.005, d=.1, s=.6, r=.1):
    e = np.ones(n) * s
    na, nd, nr = int(a * SR), int(d * SR), int(r * SR)
    na = min(na, n); e[:na] = np.linspace(0, 1, na)
    nd = min(nd, n - na); e[na:na + nd] = np.linspace(1, s, nd)
    if nr > 0 and n > nr: e[-nr:] *= np.linspace(1, 0, nr)
    return e

def expdec(n, k): return np.exp(-np.arange(n) / SR * k)

# ════════════════════════════════════════════════════════════════════════════
# MUSIC
# ════════════════════════════════════════════════════════════════════════════
def musicbox(m, d=1.2, g=1.0):
    t = tt(d); f = midi(m)
    s = (np.sin(2 * np.pi * f * t) + .45 * np.sin(2 * np.pi * f * 2.0 * t) * expdec(len(t), 9) + .25 * np.sin(2 * np.pi * f * 4.1 * t) * expdec(len(t), 18))
    return s * expdec(len(t), 4.5) * np.minimum(1, t / .003) * g

def pizz(m, d=.5):
    t = tt(d); f = midi(m)
    s = np.sin(2 * np.pi * f * t) + .5 * np.sin(2 * np.pi * 2 * f * t) + .25 * np.sin(2 * np.pi * 3 * f * t)
    return lp(s * expdec(len(t), 9), 1200) * np.minimum(1, t / .004)

def pad(ms, d, g=1.0, bright=900):
    t = tt(d); s = np.zeros(len(t))
    for m in ms:
        f = midi(m)
        for det in (-0.12, 0.0, 0.12):
            ph = rng.uniform(0, 6)
            s += np.sign(np.sin(2 * np.pi * f * (1 + det / 100) * t + ph)) * .25 + np.sin(2 * np.pi * f * t + ph)
    s = lp(s, bright)
    return s * adsr(len(t), .4, .3, .8, .6) * g / (len(ms) * 3)

# spooky waltz, D minor, 3/4 @ 100bpm
BEAT = .6; BAR = 1.8; W0 = .8
CH = [(50, 53, 57), (50, 53, 57), (43, 46, 50), (45, 49, 52), (50, 53, 57), (46, 50, 53), (43, 46, 50), (45, 49, 52)]
MEL = [(81, 77, 74), (73, 74, 76), (82, 81, 79), (81, 79, 76), (77, 81, 86), (86, 82, 77), (79, 81, 82), (85, 81, None)]
end_waltz = 17.0
bar = 0
t0 = W0
while t0 < end_waltz:
    ci = bar % 8
    root = CH[ci][0]
    # drone + pizz bass
    add(t0, pizz(root - 12, .9), .55, -.1, 'mus')
    for b in (1, 2):
        for m in CH[ci][1:]:
            add(t0 + b * BEAT, pizz(m, .35) * .5, .22, .2, 'mus')
    for b, m in enumerate(MEL[ci]):
        if m is None: continue
        g = .32 if t0 < 9 else .26
        add(t0 + b * BEAT + (0.01 * b), musicbox(m, 1.6), g, .25, 'mus')
    if bar >= 4:   # pad comes in for the lobby
        add(t0, pad([m + 12 for m in CH[ci]], BAR + .4, 1, 1400), .18 if t0 > 9 else .1, 0, 'mus')
    t0 += BAR; bar += 1
# low drone under the exterior
t = tt(9.5)
drone = (np.sin(2 * np.pi * midi(38) * t) + .6 * np.sin(2 * np.pi * midi(45) * t + 1)) * (.7 + .3 * np.sin(2 * np.pi * .25 * t))
drone *= np.minimum(1, t / 1.5) * np.minimum(1, (9.5 - t) / 1.5)
add(0, drone, .12, 0, 'mus')

# phones section 17–21: light ticking + suspense pad, then a happy lift on the check
for k in range(int((21.0 - 17.45) / .3)):
    tk = 17.45 + k * .3
    if tk > 18.45: break
    tick = bp(rng.standard_normal(int(.03 * SR)), 2500, 6000) * expdec(int(.03 * SR), 140)
    add(tk, tick, .25, .3, 'mus')
add(17.4, pad([62, 65, 69], 1.2, 1, 1200), .14, 0, 'mus')
add(19.5, pad([62, 66, 69, 74], 1.6, 1, 1800), .2, 0, 'mus')

# party groove, D minor funk @ 124
PB = 60 / 124
P0 = 21.15
def kick():
    t = tt(.35); f = 120 * np.exp(-t * 22) + 45
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * expdec(len(t), 9)
def snare():
    t = tt(.22)
    return (bp(rng.standard_normal(len(t)), 1500, 7000) * .8 + np.sin(2 * np.pi * 190 * t) * .5) * expdec(len(t), 22)
def hat(open_=False):
    n = int((.18 if open_ else .05) * SR)
    return hp(rng.standard_normal(n), 7000) * expdec(n, 12 if open_ else 70)
def bassnote(m, d):
    t = tt(d); f = midi(m)
    s = np.sign(np.sin(2 * np.pi * f * t)) * .5 + np.sin(2 * np.pi * f * t)
    return lp(s, 700) * adsr(len(t), .004, .08, .6, .04)
def organ(ms, d):
    t = tt(d); s = np.zeros(len(t))
    for m in ms:
        f = midi(m)
        s += np.sin(2 * np.pi * f * t) + .5 * np.sin(2 * np.pi * 2 * f * t) + .3 * np.sin(2 * np.pi * 3 * f * t)
    return s * adsr(len(t), .005, .05, .7, .05) / len(ms)
PCH = [(62, 65, 69, 72), (58, 62, 65, 69), (60, 64, 67, 70), (57, 61, 64, 67)]
PBASS = [(38, 0, .4), (38, .75, .2), (41, 1, .4), (43, 1.5, .4), (45, 2, .4), (43, 2.75, .2), (41, 3, .4), (40, 3.5, .4)]
party_L = np.zeros(N); party_R = np.zeros(N)
def padd(t0, sig, g, pan=0):
    i = int(t0 * SR)
    if i >= N: return
    sig = sig[:N - i]
    party_L[i:i + len(sig)] += sig * g * np.sqrt((1 - pan) / 2); party_R[i:i + len(sig)] += sig * g * np.sqrt((1 + pan) / 2)
b = 0; tb = P0
while tb < DUR:
    for q in range(4):
        tq = tb + q * PB
        padd(tq, kick(), .9)
        if q in (1, 3): padd(tq, snare(), .5, .1)
        padd(tq, hat(), .18, .35); padd(tq + PB / 2, hat(q == 3), .14, .35)
    ci = b % 4
    for (m, off, d) in PBASS:
        padd(tb + off * PB, bassnote(m + (0 if ci in (0, 3) else (-4 if ci == 1 else -2)), d * PB * 1.9), .5, -.1)
    for off in (.5, 1.5, 2.5, 3.25):
        padd(tb + off * PB, organ(PCH[ci], .22), .22, .25)
    tb += 4 * PB; b += 1
# muffled through the doors, opens as he walks in, then ducks for the logo
t_all = np.arange(N) / SR
muffle = np.clip((t_all - 23.6) / .9, 0, 1)
muffle = np.where(t_all > 25.5, np.clip(1 - (t_all - 25.5) / .7, 0, 1), muffle)
pl = party_L * muffle + lp(party_L, 650) * (1 - muffle)
pr = party_R * muffle + lp(party_R, 650) * (1 - muffle)
vol = np.clip((t_all - 21.15) / .2, 0, 1) * np.where(t_all > 25.5, np.clip(1 - (t_all - 25.5) / 1.6, .25, 1), 1.0)
vol *= np.where(t_all > 28.0, np.clip(1 - (t_all - 28.0) / .8, 0, 1), 1)
MUS_L += pl * vol * .5; MUS_R += pr * vol * .5

# final sting: D major (the ghost is home)
def bell(m, d=3.0):
    t = tt(d); f = midi(m)
    s = sum(a * np.sin(2 * np.pi * f * r * t) * expdec(len(t), k) for (r, a, k) in ((1, 1, 1.6), (2.0, .5, 2.4), (2.76, .3, 3.5), (5.4, .15, 6)))
    return s * np.minimum(1, t / .002)
for i, m in enumerate((74, 78, 81, 86)):
    add(28.25 + i * .06, bell(m, 2.6), .22, -.3 + i * .2, 'mus')
add(28.25, pad([50, 57, 62, 66, 69], 1.9, 1, 2200), .3, 0, 'mus')
add(28.25, pizz(38, 1.5), .6, 0, 'mus')

# ════════════════════════════════════════════════════════════════════════════
# SFX
# ════════════════════════════════════════════════════════════════════════════
# wind
t = tt(9.4); wn = bp(rng.standard_normal(len(t)), 250, 900)
wn *= (.5 + .5 * np.sin(2 * np.pi * .18 * t) ** 2) * np.minimum(1, t / 1.0) * np.minimum(1, (9.4 - t) / 1.2)
wn *= np.where(t > 5.5, .45, 1.0)
add(0, wn, .22, -.2); add(0, wn[::-1].copy(), .16, .3)
# owl
def hoot(d=.42):
    t = tt(d); f = 380 + 25 * np.sin(2 * np.pi * 6 * t) - 40 * t
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.sin(np.pi * t / d) ** 1.5
add(.7, hoot(), .18, .6); add(1.15, hoot(.6), .18, .6)
# car engine + gravel
t = tt(2.6)
speed = np.clip(np.where(t < .2, 0, 1 - ((t - .2) / 2.2) ** 2), 0, 1)
f = 42 + 38 * speed
ph = np.cumsum(f) / SR
eng = sum((1 / k) * np.sin(2 * np.pi * k * ph + k) for k in range(1, 8))
eng = lp(eng * (1 + .3 * np.sin(2 * np.pi * 15 * t)), 500) * (.3 + .7 * speed) * np.minimum(1, t / .4) * np.minimum(1, (2.6 - t) / .3)
add(0, eng, .32, -.4)
grav = bp(rng.standard_normal(len(t)), 1500, 5000) * speed * (.6 + .4 * np.abs(np.sin(2 * np.pi * 9 * t)))
add(0, grav, .12, -.4)
t = tt(.3); sq = np.sin(2 * np.pi * np.cumsum(2200 - 600 * t + 40 * np.sin(2 * np.pi * 30 * t)) / SR) * np.sin(np.pi * t / .3)
add(2.25, sq, .05, -.2)
def thud(d=.25, f0=90, k=18, nz=.4):
    t = tt(d)
    return (np.sin(2 * np.pi * np.cumsum(f0 * np.exp(-t * 4)) / SR) + nz * lp(rng.standard_normal(len(t)), 900)) * expdec(len(t), k)
click = bp(rng.standard_normal(int(.02 * SR)), 2000, 6000) * expdec(int(.02 * SR), 200)
add(2.6, click, .3, -.3)
add(3.17, thud(.4, 110, 14, .8), .7, -.3)
def step_gravel():
    n = int(.12 * SR); return bp(rng.standard_normal(n), 1200, 5000) * expdec(n, 30)
def step_wood():
    return thud(.16, 140, 30, .5)
for k in range(3):
    add(3.05 + k * .35, step_gravel(), .22, -.2)
for k in range(2):
    add(4.9 + k * .35, step_gravel(), .18, 0)
# fear stinger
t = tt(.9)
st = np.zeros(len(t))
for m in (86, 87, 89, 92):
    f = midi(m) * (1 + .004 * np.sin(2 * np.pi * 7 * t))
    st += np.sign(np.sin(2 * np.pi * np.cumsum(f) / SR)) * .3
st = bp(st, 600, 5000) * np.minimum(1, t / .05) * expdec(len(t), 3.2) * (1 + .4 * np.sin(2 * np.pi * 14 * t))
add(3.98, st, .2, 0)
t = tt(.18); gulp = np.sin(2 * np.pi * np.cumsum(500 * np.exp(-t * 12) + 120) / SR) * np.sin(np.pi * t / .18)
add(4.5, gulp, .35, 0)
# porch: footsteps, ghost giggles, creak, bloom
for k in range(6):
    add(5.6 + k * .37, step_wood(), .3, -.3 + k * .06)
def giggle(n=4, base=1000):
    out = []
    for i in range(n):
        t = tt(.11); f = base * (1.15 - .1 * i) + 120 * np.sin(2 * np.pi * 18 * t)
        out.append(np.sin(2 * np.pi * np.cumsum(f) / SR) * np.sin(np.pi * t / .11) ** 2)
        out.append(np.zeros(int(.04 * SR)))
    return np.concatenate(out)
add(6.2, giggle(4, 1100), .1, .7); add(6.9, giggle(3, 1300), .08, .8); add(7.7, giggle(5, 1000), .09, .7)
t = tt(.7)
cr = np.sin(2 * np.pi * np.cumsum(520 + 160 * np.sin(2 * np.pi * 3.3 * t) + 60 * rng.standard_normal(len(t)).cumsum() / 400) / SR)
cr = bp(cr * (1 + .5 * np.sign(np.sin(2 * np.pi * 37 * t))), 400, 3000) * np.sin(np.pi * t / .7)
add(7.25, cr, .12, 0)
def whoosh(d, lo=300, hi=4000, rise=True):
    t = tt(d); nz = rng.standard_normal(len(t)); out = np.zeros(len(t))
    seg_n = 8; step = len(t) // seg_n
    for s_ in range(seg_n):
        a = s_ / (seg_n - 1)
        fc = lo * (hi / lo) ** (a if rise else 1 - a)
        piece = bp(nz, max(60, fc * .6), min(18000, fc * 1.6))
        w = np.clip(1 - np.abs(np.arange(len(t)) - s_ * step) / step, 0, 1)
        out += piece * w
    return out * np.sin(np.pi * t / d)
add(8.4, whoosh(.7, 400, 6000), .22, 0)
# lobby: desk bell, steps, a friendly grunt, rustle
t = tt(1.6)
dbell = sum(a * np.sin(2 * np.pi * f * t) * expdec(len(t), k) for (f, a, k) in ((1870, 1, 3), (4230, .4, 6), (6100, .2, 9)))
add(9.3, dbell, .16, .2)
for k in range(6):
    add(9.4 + k * .37, step_wood(), .22, -.5 + k * .08)
for k in range(5):
    add(9.4 + k * .37 + .18, step_wood(), .14, .5)
t = tt(.55)
gr = np.sign(np.sin(2 * np.pi * np.cumsum(85 + 10 * np.sin(2 * np.pi * 2 * t)) / SR))
gr = (bp(gr, 350, 800) + .5 * bp(gr, 1000, 1600)) * np.sin(np.pi * t / .55) ** 2
add(10.7, gr, .2, .1)
add(11.85, bp(rng.standard_normal(int(.25 * SR)), 1500, 5000) * np.sin(np.pi * tt(.25) / .25), .06, .1)
add(12.9, whoosh(1.1, 200, 3000), .12, 0)
# phones
def beep(f, d=.08):
    t = tt(d); return np.sin(2 * np.pi * f * t) * np.minimum(1, t / .003) * np.minimum(1, (d - t) / .01)
add(14.3, musicbox(88, .8), .14, -.2); add(14.42, musicbox(95, .8), .12, -.2)
add(15.1, beep(2400, .05), .1, .4); add(15.22, beep(2400, .05), .1, .4)
add(17.0, whoosh(.45, 300, 5000, rise=False), .5, 0)
add(18.45, beep(1320, .09), .2, 0); add(18.55, beep(1760, .14), .2, 0)
t = tt(.14); pop = np.sin(2 * np.pi * np.cumsum(260 + 1200 * t / .14) / SR) * np.sin(np.pi * t / .14)
add(18.58, pop, .35, 0); add(18.58, thud(.15, 160, 30, .2), .3, 0)
add(19.5, thud(.2, 120, 22, .3), .55, 0)
for i, m in enumerate((86, 90, 93, 98)):
    add(19.52 + i * .05, musicbox(m, 1.0), .18, -.2 + i * .13)
# party
add(21.0, whoosh(.5, 300, 4000), .35, 0)
t = tt(1.3)
crowd = bp(rng.standard_normal(len(t)), 500, 3000) * np.minimum(1, t / .1) * expdec(len(t), 2)
add(24.35, crowd, .18, 0)
for k in range(4):
    td = tt(.4); f = rng.uniform(500, 800) + 500 * td
    add(24.4 + k * .09, np.sin(2 * np.pi * np.cumsum(f) / SR) * np.sin(np.pi * td / .4), .05, rng.uniform(-.6, .6))
# logo build
add(25.85, whoosh(.6, 250, 3500), .3, -.6)
add(26.05, whoosh(.6, 250, 3500), .3, .6)
for i in range(4):
    add(26.8 + i * .1, musicbox(91 + i * 2, .5), .12, -.2 + i * .13)
t = tt(1.25)
f = 330 + 260 * np.sin(np.pi * t / 1.25 * .9) + 14 * np.sin(2 * np.pi * 6 * t)
thr = (np.sin(2 * np.pi * np.cumsum(f) / SR) + .25 * np.sin(4 * np.pi * np.cumsum(f) / SR)) * np.sin(np.pi * t / 1.25) ** .8
add(27.15, thr, .16, -.2)
for i in range(28):
    add(28.2 + i * .028, beep(3000 + (i % 5) * 300, .015), .02, rng.uniform(-.5, .5))

# ════════════════════════════════════════════════════════════════════════════
# mix
# ════════════════════════════════════════════════════════════════════════════
# light room reverb on the music bus
ir_t = tt(1.4); ir = rng.standard_normal(len(ir_t)) * expdec(len(ir_t), 4.5); ir = lp(ir, 4000); ir /= np.sqrt((ir ** 2).sum())
ML = MUS_L + .35 * fftconvolve(MUS_L, ir)[:N]; MR = MUS_R + .35 * fftconvolve(MUS_R, ir[::-1].copy())[:N]
mixL = ML * .9 + L; mixR = MR * .9 + R
fade = np.ones(N); fn = int(.25 * SR); fade[-fn:] = np.linspace(1, 0, fn); fade[:int(.3 * SR)] = np.linspace(0, 1, int(.3 * SR))
mixL *= fade; mixR *= fade
pk = max(np.abs(mixL).max(), np.abs(mixR).max())
mixL = np.tanh(mixL / pk * 1.3) / np.tanh(1.3) * .89
mixR = np.tanh(mixR / pk * 1.3) / np.tanh(1.3) * .89
import os
from common import BUILD
wavfile.write(os.path.join(BUILD, 'soundtrack.wav'), SR, (np.stack([mixL, mixR], 1) * 32767).astype(np.int16))
print('ok', pk)
