"""Real instruments, played note by note, from the recordings installed with Logic Pro and GarageBand.

Three kinds of source, one kind of instrument:
  - a folder of single notes (Logic's orchestral samples, the steel-string guitar, the grand piano);
  - a consolidated note set (Logic's erhu, guzheng and pipa): split where it goes quiet;
  - a loop (banjo, fiddle, harmonica, accordion, oud, dobro, dizi): split at its note onsets.
Every piece is pitch-tracked and kept only if it holds one steady pitch, so a "note" is a note that was
really played. An Instrument then plays any pitch from the nearest recorded one (repitched by
resampling, so a few semitones at most), stretching a sustained note by crossfading its middle.

All of it is decoded once into build/snd (afconvert) and the banks are cached in build/snd/banks.
"""
import functools, glob, hashlib, math, os, pickle, re, subprocess
import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, sosfilt

SR = 44100
HERE = os.path.dirname(os.path.abspath(__file__))
SND = os.path.join(HERE, 'build', 'snd')
LIB = os.path.expanduser('~/Music/Logic Pro Library.bundle')
LEG = f'{LIB}/Samples/z_Legacy'
AL = '/Library/Audio/Apple Loops/Apple'
FX = f'{AL}/Final Cut Pro Sound Effects'


def decode(path):
    """Any audio file as float stereo at 44.1 kHz."""
    os.makedirs(SND, exist_ok=True)
    h = hashlib.md5(path.encode()).hexdigest()[:12]
    wav = os.path.join(SND, f'{h}.wav')
    if not os.path.exists(wav):
        subprocess.run(['afconvert', '-f', 'WAVE', '-d', 'LEI16@44100', '-c', '2', path, wav], check=True,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    _, x = wavfile.read(wav)
    x = x.astype(np.float32) / 32768
    return x if x.ndim == 2 else np.stack([x, x], 1)


def hp(x, f): return sosfilt(butter(2, f, 'high', fs=SR, output='sos'), x, axis=0)
def lp(x, f): return sosfilt(butter(2, f, 'low', fs=SR, output='sos'), x, axis=0)
def bp(x, a, b): return sosfilt(butter(2, [a, b], 'band', fs=SR, output='sos'), x, axis=0)


# ── pitch ─────────────────────────────────────────────────────────────────────
def f0_track(mono, hop=1024, win=4096, fmin=30, fmax=1800):
    """(midi, confidence) per frame, by normalized autocorrelation."""
    out = []
    lo, hi = int(SR / fmax), int(SR / fmin)
    for i in range(0, len(mono) - win, hop):
        s = mono[i:i + win] * np.hanning(win)
        if np.sqrt((s ** 2).mean()) < 1e-3: out.append((np.nan, 0)); continue
        ac = np.fft.irfft(np.abs(np.fft.rfft(s, 2 * win)) ** 2)[:win]
        if ac[0] <= 0: out.append((np.nan, 0)); continue
        ac = ac / ac[0]
        k = lo + int(np.argmax(ac[lo:hi]))
        # parabolic refinement
        if 1 <= k < len(ac) - 1:
            a, b, c = ac[k - 1], ac[k], ac[k + 1]; den = a - 2 * b + c
            d = float(np.clip(.5 * (a - c) / den, -.5, .5)) if abs(den) > 1e-12 else 0.0
            conf = float(ac[k]); k = k + d
        else:
            conf = float(ac[k])
        out.append((69 + 12 * math.log2(SR / max(k, 1) / 440), conf))
    return np.array(out) if out else np.zeros((0, 2))


def steady_pitch(x, min_conf=.75):
    """The note a piece holds, or None if it does not hold one."""
    tr = f0_track(x.mean(1))
    if len(tr) < 3: return None
    ok = tr[:, 1] > min_conf
    if ok.sum() < max(3, .5 * len(tr)): return None
    med = np.median(tr[ok, 0])
    near = np.abs(tr[ok, 0] - med) < .4
    if near.mean() < .75: return None
    return float(np.median(tr[ok, 0][near]))


def hps_pitch(x, fmin=40, fmax=1500):
    """A second opinion, by harmonic product spectrum over half a second."""
    m = x.mean(1)[:int(.5 * SR)]; n = 1 << 16
    X = np.abs(np.fft.rfft(m * np.hanning(len(m)), n)); f = np.fft.rfftfreq(n, 1 / SR); P = X.copy()
    for h in (2, 3, 4): P[:len(X) // h] *= X[::h][:len(X) // h]
    lo, hi = np.searchsorted(f, [fmin, fmax]); k = lo + int(np.argmax(P[lo:hi]))
    return 69 + 12 * math.log2(max(f[k], 1) / 440)


# ── cutting sources into notes ────────────────────────────────────────────────
def by_silence(x, thresh=.004, min_len=.25):
    e = np.sqrt(np.convolve(x.mean(1) ** 2, np.ones(441) / 441, 'same'))
    on = (e > thresh).astype(np.int8); d = np.diff(on)
    st = list(np.flatnonzero(d == 1)); en = list(np.flatnonzero(d == -1))
    if on[0]: st = [0] + st
    if on[-1]: en = en + [len(x) - 1]
    return [(a, b) for a, b in zip(st, en) if b - a > min_len * SR]


def by_onsets(x, min_len=.18, max_len=2.5):
    """Spectral-flux onsets; each note runs to the next onset."""
    mono = x.mean(1); hop = 512; win = 2048
    frames = [np.abs(np.fft.rfft(mono[i:i + win] * np.hanning(win))) for i in range(0, len(mono) - win, hop)]
    if len(frames) < 4: return []
    S = np.log1p(np.array(frames))
    flux = np.clip(np.diff(S, axis=0), 0, None).sum(1)
    flux = (flux - np.median(flux)) / (flux.std() + 1e-9)
    peaks = [i + 1 for i in range(1, len(flux) - 1) if flux[i] > 1.5 and flux[i] >= flux[i - 1] and flux[i] >= flux[i + 1]]
    on = []
    for p in peaks:
        if not on or (p - on[-1]) * hop > .09 * SR: on.append(p)
    pts = [p * hop for p in on] + [len(mono)]
    return [(a, min(b, a + int(max_len * SR))) for a, b in zip(pts[:-1], pts[1:]) if b - a > min_len * SR]


def bank_of(name, sources, cut, keep=None):
    """[(midi, audio)] for an instrument, cached."""
    cache = os.path.join(SND, 'banks', f'{name}.pkl')
    if os.path.exists(cache): return pickle.load(open(cache, 'rb'))
    notes = []
    for path in sources:
        x = decode(path)
        segs = [(0, len(x))] if cut == 'whole' else by_silence(x) if cut == 'silence' else by_onsets(x)
        for a, b in segs:
            y = x[max(0, a - 64):b].copy()
            if len(y) < .15 * SR: continue
            p = steady_pitch(y[:int(min(len(y), 1.5 * SR))])
            if p is None: continue
            # a cut from a loop can carry a neighbor's ringing string: keep it only if the two ways of
            # hearing its pitch agree (they disagree by octaves and fifths, which is exactly the risk)
            if cut != 'whole' and abs(hps_pitch(y) - p) > .5: continue
            pk = np.abs(y).max()
            if pk < .02: continue
            notes.append((p, (y / pk).astype(np.float32)))
    if keep: notes = keep(notes)
    os.makedirs(os.path.dirname(cache), exist_ok=True)
    pickle.dump(notes, open(cache, 'wb'))
    return notes


_NOTE = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}

def label_of(path):
    """The note a file's name says, in standard numbering (C4 = 60) — or None."""
    b = os.path.splitext(os.path.basename(path))[0]
    mm = re.search(r'(?:^|[_\s-])([A-G])(#|b)?(-?\d)$', b)
    if mm:
        return 12 * (int(mm.group(3)) + 1) + _NOTE[mm.group(1)] + (1 if mm.group(2) == '#' else -1 if mm.group(2) == 'b' else 0)
    mm = re.search(r'(?:^n|\sn)(\d{2,3})$', b) or re.match(r'^(\d{3})_', b)
    return int(mm.group(1)) if mm else None


def bank_labeled(name, paths, pick=None):
    """A folder of single notes named by pitch. The name gives the note; measuring settles the folder's
    octave convention (Logic calls middle C 'C3'), so low notes the tracker cannot hear still land right."""
    cache = os.path.join(SND, 'banks', f'{name}.pkl')
    if os.path.exists(cache): return pickle.load(open(cache, 'rb'))
    raw = []
    for p in paths:
        lab = label_of(p)
        if lab is None: continue
        x = decode(p); pk = np.abs(x).max()
        if pk < .01: continue
        raw.append((lab, (x / pk).astype(np.float32)))
    offs, meas = [], []
    for lab, x in raw:
        p = steady_pitch(x[:int(1.5 * SR)], .6); meas.append(p)
        if p is not None: offs.append(12 * round((p - lab) / 12))
    off = int(np.median(offs)) if offs else 0
    # the name says which note; where the recording measurably sits a little off it, tune to what it is
    def true_pitch(lab, x, p):
        n0 = lab + off
        if p is not None and abs(p - n0) < .5: return p
        h = hps_pitch(x)                      # a file filed under the wrong octave: go by what it plays
        if abs(h - n0) > 6 and abs(h - round(h)) < .3 and abs((h - n0) % 12 - 0) < .4 or abs((h - n0) % 12 - 12) < .4:
            return h
        return n0
    notes = sorted(((true_pitch(lab, x, p), x) for (lab, x), p in zip(raw, meas)), key=lambda t: t[0])
    if pick: notes = pick(notes)
    os.makedirs(os.path.dirname(cache), exist_ok=True); pickle.dump(notes, open(cache, 'wb'))
    return notes


# ── EastWest instruments, rendered through Opus in Logic ───────────────────
# build/ew/Inst N.aif: every semitone C1..C7 (MIDI 24-96), note n held 3 s from 2 + (n-24)*4 s, then 1 s to
# ring out (build/ew/notes-C1-C7.mid). EW_TRACKS says which instrument each Logic track holds.
EW_DIR = os.path.join(HERE, 'build', 'ew')
EW_TRACKS = {
    'ew_banjo': 1, 'ew_harmonica': 2, 'ew_accordion': 3, 'ew_oud': 4, 'ew_oud_trem': 5, 'ew_ney': 6, 'ew_erhu': 7,
    'ew_guzheng': 8, 'ew_pipa': 9, 'ew_dizi': 10, 'ew_fiddle': 11, 'ew_horns': 12, 'ew_trumpets': 13, 'ew_trombones': 14,
    'ew_tuba': 15, 'ew_choir_m': 16, 'ew_choir_f': 17, 'ew_renflutes': 18, 'ew_whistle': 19, 'ew_gtr_pick': 20,
    'ew_gtr_fngr': 21, 'ew_dobro': 22, 'ew_violins': 23, 'ew_violas': 24, 'ew_cellos': 25, 'ew_basses': 26,
    'ew_steinway': 27, 'ew_harp': 28, 'ew_celesta': 29, 'ew_lutes': 30, 'ew_fielddrum': 31, 'ew_bassdrum': 32, 'ew_cymbals': 33,
}

def ew_notes(track):
    """[(midi, audio)] for every key of an EW track that sounds."""
    cache = os.path.join(SND, 'banks', f'ew{track}.pkl')
    if os.path.exists(cache): return pickle.load(open(cache, 'rb'))
    x = decode(os.path.join(EW_DIR, f'Inst {track}.aif'))
    notes = []
    for n in range(24, 97):
        a = int((2 + (n - 24) * 4) * SR); b = a + int(4 * SR)
        y = x[a:b]
        if len(y) < SR or np.abs(y).max() < .003: continue
        # trim the silence before the attack (a few ms of latency)
        e = np.abs(y).max(1); on = int(np.argmax(e > .02 * e.max()))
        y = y[max(0, on - 32):]
        notes.append((float(n), (y / np.abs(y).max()).astype(np.float32)))
    os.makedirs(os.path.dirname(cache), exist_ok=True); pickle.dump(notes, open(cache, 'wb'))
    return notes


# ── playing ───────────────────────────────────────────────────────────────────
def repitch(x, semis):
    if abs(semis) < 1e-3: return x
    r = 2 ** (semis / 12); idx = np.arange(0, len(x) - 1, r)
    return np.stack([np.interp(idx, np.arange(len(x)), x[:, c]) for c in (0, 1)], 1).astype(np.float32)


class Instrument:
    def __init__(self, notes, sustain=False, att=0.0, rel=.25, gain=1.0, prefer_long=True):
        if not notes: raise ValueError('an instrument with no notes')
        self.notes = notes; self.sustain = sustain; self.att = att; self.rel = rel; self.gain = gain
        self.prefer_long = prefer_long
        self.rng = np.random.default_rng(len(notes))

    def range(self):
        ms = [m for m, _ in self.notes]; return min(ms), max(ms)

    def pick(self, midi, dur):
        def cost(n):
            m, x = n
            c = abs(m - midi)
            if self.prefer_long and len(x) < (dur + self.rel) * SR: c += .6 * (1 - len(x) / ((dur + self.rel) * SR))
            return c + self.rng.uniform(0, .25)        # round-robin among near equals: no machine-gun repeats
        return min(self.notes, key=cost)

    def note(self, midi, dur, vel=.8, att=None, rel=None):
        att = self.att if att is None else att; rel = self.rel if rel is None else rel
        m, x = self.pick(midi, dur)
        x = repitch(x, midi - m)
        L = int((dur + rel) * SR)
        if len(x) < L and self.sustain:              # stretch a held note: crossfade its middle onto itself
            body = x[int(len(x) * .25):int(len(x) * .8)]
            xf = max(64, min(int(.12 * SR), len(body) // 3))
            y = x.copy()
            while len(y) < L and len(body) > 2 * xf:
                w = np.linspace(0, 1, xf)[:, None]
                y = np.concatenate([y[:-xf], y[-xf:] * (1 - w) + body[:xf] * w, body[xf:]])
            x = y
        x = x[:L].copy()
        if len(x) < L: x = np.concatenate([x, np.zeros((L - len(x), 2), np.float32)])
        e = np.ones(L, np.float32)
        if att > 0: k = min(L, int(att * SR)); e[:k] = np.linspace(0, 1, k) ** 2
        r0 = int(dur * SR)
        if L > r0: e[r0:] *= np.linspace(1, 0, L - r0) ** 1.6
        return x * e[:, None] * vel * self.gain


# ── the instruments ───────────────────────────────────────────────────────────
def files(folder, pat='*'):
    return sorted(f for f in glob.glob(os.path.join(folder, pat)) if f.lower().endswith(('.wav', '.aif', '.aiff', '.caf')))

def loops(*names):
    out = []
    for n in names:
        out += [l for l in open('/tmp/claude_snd_all.txt').read().splitlines() if os.path.basename(l).startswith(n)] \
            if os.path.exists('/tmp/claude_snd_all.txt') else []
    if not out:
        for n in names:
            out += glob.glob(f'{AL}/**/{n}*.caf', recursive=True) + glob.glob(f'{LIB}/Apple Loops/**/{n}*.caf', recursive=True)
    return sorted(set(out))

def one_per_semitone(notes, prefer='long'):
    best = {}
    for m, x in notes:
        k = round(m)
        if abs(m - k) > .3: continue
        if k not in best or (len(x) > len(best[k][1]) if prefer == 'long' else np.abs(x[:2000]).max() > np.abs(best[k][1][:2000]).max()):
            best[k] = (m, x)
    return [best[k] for k in sorted(best)]

def several_per_semitone(notes, n=3):
    by = {}
    for m, x in notes:
        k = round(m)
        if abs(m - k) <= .3: by.setdefault(k, []).append((m, x))
    out = []
    for k in sorted(by): out += sorted(by[k], key=lambda t: -len(t[1]))[:n]
    return out


@functools.lru_cache(None)
def get(name):
    if name in EW_TRACKS:
        held = {'ew_harmonica', 'ew_accordion', 'ew_ney', 'ew_erhu', 'ew_dizi', 'ew_fiddle', 'ew_horns', 'ew_trumpets',
                'ew_trombones', 'ew_choir_m', 'ew_choir_f', 'ew_renflutes', 'ew_whistle', 'ew_violins', 'ew_violas',
                'ew_cellos', 'ew_basses', 'ew_oud_trem'}
        return Instrument(ew_notes(EW_TRACKS[name]), sustain=name in held, att=.0, rel=.35 if name in held else .6,
                          prefer_long=name in held)
    O = f'{LEG}/Orchestral'
    folder = lambda d: bank_labeled(name, files(d))
    if name == 'trumpets':   return Instrument(folder(f'{O}/Trumpets/Trumpets_nA_sus_f'), sustain=True, att=.03, rel=.3)
    if name == 'trumpet_st': return Instrument(folder(f'{O}/Trumpets/Trumpets_stac_f1'), att=0, rel=.15, prefer_long=False)
    if name == 'horns':      return Instrument(folder(f'{O}/French Horns/French Horns_nA_sus_f'), sustain=True, att=.05, rel=.4)
    if name == 'trombones':  return Instrument(folder(f'{O}/Trombones/' + os.listdir(f'{O}/Trombones')[0]), sustain=True, att=.04, rel=.35)
    if name == 'tuba':       return Instrument(folder(f'{O}/Tuba Solo/Tuba_stac_mf1'), att=0, rel=.12, prefer_long=False)
    if name == 'choir_f':    return Instrument(folder(f'{O}/Choir/Classical Choir/Classical Choir Female'), sustain=True, att=.15, rel=.6)
    if name == 'choir_m':    return Instrument(folder(f'{O}/Choir/Classical Choir/Classical Choir Male'), sustain=True, att=.15, rel=.6)
    if name == 'flute':      return Instrument(folder(f'{O}/Flute Solo/Flute_LV_na_sus_mf'), sustain=True, att=.04, rel=.25)
    if name == 'flute_st':   return Instrument(folder(f'{O}/Flute Solo/Flute_stac_f1'), att=0, rel=.1, prefer_long=False)
    if name == 'clarinet':   return Instrument(folder(f'{O}/Clarinet Solo/Clarinet-L_pA_sus_mf'), sustain=True, att=.03, rel=.25)
    if name == 'oboe':       return Instrument(folder(f'{O}/Oboe Solo/' + os.listdir(f'{O}/Oboe Solo')[0]), sustain=True, att=.03, rel=.25)
    if name == 'harp':       return Instrument(folder(f'{O}/Harp/Harp_ES_mf'), att=0, rel=1.2)
    if name == 'organ':      return Instrument(folder(f'{O}/Church Organ/' + os.listdir(f'{O}/Church Organ')[0]), sustain=True, att=.05, rel=.6)
    if name == 'guitar':     return Instrument(folder(f'{LEG}/Guitar/Steel String Acoustic Guitar'), att=0, rel=.4)
    if name == 'piano':      return Instrument(bank_labeled(name, [f for f in files(f'{LEG}/Keyboard/Grand Piano') if f.endswith('_M.wav')]), att=0, rel=.5)
    if name == 'erhu':       return Instrument(bank_of(name, [f'{LIB}/Samples/Other Strings/Erhu_consolidated.caf'], 'silence', one_per_semitone), sustain=True, att=.06, rel=.3)
    if name == 'guzheng':    return Instrument(bank_of(name, [f'{LIB}/Samples/Other Strings/Guzheng_consolidated.caf'], 'silence', lambda n: one_per_semitone(n, 'loud')), att=0, rel=1.0)
    if name == 'pipa':       return Instrument(bank_of(name, [f'{LIB}/Samples/Other Strings/Pipa Main_consolidated.caf'], 'silence', lambda n: one_per_semitone(n, 'loud')), att=0, rel=.6)
    if name == 'banjo':      return Instrument(bank_of(name, loops('Rural Banjo'), 'onsets', several_per_semitone), att=0, rel=.25, prefer_long=False)
    if name == 'fiddle':     return Instrument(bank_of(name, loops('Southern Fiddle'), 'onsets', several_per_semitone), sustain=True, att=.02, rel=.2)
    if name == 'harmonica':  return Instrument(bank_of(name, loops('Blues Harmonica'), 'onsets', several_per_semitone), sustain=True, att=.03, rel=.2)
    if name == 'accordion':  return Instrument(bank_of(name, loops('Blue Sky Accordion', 'Warm Sun Accordion'), 'onsets', several_per_semitone), sustain=True, att=.03, rel=.25)
    if name == 'oud':        return Instrument(bank_of(name, loops('Middle Eastern Oud', 'Middle Eastern Lute'), 'onsets', several_per_semitone), att=0, rel=.35, prefer_long=False)
    if name == 'dobro':      return Instrument(bank_of(name, loops('Front Porch Dobro'), 'onsets', several_per_semitone), att=0, rel=.4)
    if name == 'dizi':       return Instrument(bank_of(name, loops('Chinese Traditional Dizi'), 'onsets', several_per_semitone), sustain=True, att=.03, rel=.25)
    if name == 'mandolin':   return Instrument(bank_of(name, loops('Cheerful Mandolin'), 'onsets', several_per_semitone), att=0, rel=.3, prefer_long=False)
    S_ = f'{LIB}/Samples'; AL_ = f'{S_}/Alchemy Samples'
    cons = lambda path, keep=one_per_semitone: bank_of(name, [path], 'silence', keep)
    wavs = lambda d: bank_labeled(name, [f for f in files(d) if f.endswith(('.wav', '.caf'))])
    if name == 'st_trumpet':  return Instrument(cons(f'{S_}/Brass/Studio Horns/Studio Trumpet 1_consolidated.caf'), sustain=True, att=.02, rel=.25)
    if name == 'st_trombone': return Instrument(cons(f'{S_}/Brass/Studio Horns/Studio Trombone 1_consolidated.caf'), sustain=True, att=.03, rel=.3)
    if name == 'st_violins':  return Instrument(cons(f'{S_}/Strings/Studio Strings/Studio Violins 1_consolidated.caf'), sustain=True, att=.08, rel=.5)
    if name == 'st_violas':   return Instrument(cons(f'{S_}/Strings/Studio Strings/Studio Violas_consolidated.caf'), sustain=True, att=.08, rel=.5)
    if name == 'st_cellos':   return Instrument(cons(f'{S_}/Strings/Studio Strings/Studio Cellos_consolidated.caf'), sustain=True, att=.08, rel=.5)
    if name == 'st_basses':   return Instrument(cons(f'{S_}/Strings/Studio Strings/Studio Double Basses_consolidated.caf'), sustain=True, att=.08, rel=.5)
    if name == 'steinway':    return Instrument(cons(f'{S_}/Keyboard/Acoustic Piano/Steinway Piano 2_consolidated.caf', lambda n: one_per_semitone(n, 'loud')), att=0, rel=.6)
    if name == 'ac_guitar':   return Instrument(cons(f'{S_}/Guitar/Acoustic Guitar_consolidated.caf', lambda n: one_per_semitone(n, 'loud')), att=0, rel=.5)
    if name == 'lute':        return Instrument(cons(f'{S_}/Guitar/Classical Acoustic Guitar_consolidated.caf', lambda n: one_per_semitone(n, 'loud')), att=0, rel=.6)
    if name == 'steel':       return Instrument(wavs(f'{AL_}/Guitars/Acoustic Guitars/Steel String'), att=0, rel=.5)
    if name == 'woodflute':   return Instrument(wavs(f'{AL_}/Woodwinds/World Woodwinds/Natural Wood Flute'), sustain=True, att=.03, rel=.2)
    if name == 'lowflute':    return Instrument(wavs(f'{AL_}/Woodwinds/World Woodwinds/Mystical Low Flute'), sustain=True, att=.06, rel=.3)
    if name == 'dulcimer':    return Instrument(wavs(f'{AL_}/Guitars/Plucked Strings/App Dulcimer'), att=0, rel=.6)
    SY = f'{LIB}/Samples/Synthesizer'
    one = lambda path, root: [(root, (lambda x: (x / (np.abs(x).max() or 1)).astype(np.float32))(decode(path)))]
    if name == 'celesta':    return Instrument(bank_of(name, [f'{LIB}/Samples/Mallet/Toy Celesta_consolidated.caf'], 'silence', lambda n: one_per_semitone(n, 'loud')), att=0, rel=1.0)
    if name == 'vibes':      return Instrument(bank_of(name, [f'{LIB}/Samples/Mallet/Vibraphone_consolidated.caf'], 'silence', lambda n: one_per_semitone(n, 'loud')), att=0, rel=1.2)
    if name == 'glock':      return Instrument(bank_of(name, [f'{LIB}/Samples/Mallet/Toy Glockenspiel_consolidated.caf'], 'silence', lambda n: one_per_semitone(n, 'loud')), att=0, rel=1.0)
    if name == 'synthlead':  return Instrument(bank_labeled(name, files(f'{SY}/Keys', 'Shelburne Road Anthem Synth_C*')), sustain=True, att=.01, rel=.25)
    if name == 'synthkeys':  return Instrument(bank_labeled(name, files(f'{SY}/Keys', 'Kenmare Heroic Synth_C*')), sustain=True, att=.02, rel=.4)
    if name == 'pluck':      return Instrument(one(f'{SY}/Keys/Lime Drops Synth Pluck.aif', 65.88), att=0, rel=.2, prefer_long=False)
    if name == 'synthbass':  return Instrument(one(f'{SY}/Bass/Pluck Synth Bass.aif', 36.03), att=0, rel=.15, prefer_long=False)
    if name == 'bells':      return Instrument(one(f'{SY}/Keys/Fairytales Bells Sample.aif', 62.14), att=0, rel=1.5)
    raise KeyError(name)


def hit(path, gain=1.0):
    """A one-shot (drum, cymbal, effect) as it was recorded."""
    x = decode(path); pk = np.abs(x).max() or 1
    return x / pk * gain


if __name__ == '__main__':
    import sys
    for n in sys.argv[1:] or ['trumpets', 'horns', 'tuba', 'choir_f', 'flute', 'clarinet', 'harp', 'guitar', 'piano',
                               'erhu', 'guzheng', 'banjo', 'fiddle', 'harmonica', 'accordion', 'oud', 'dobro', 'dizi']:
        try:
            ins = get(n); lo, hi = ins.range()
            print(f'{n:10} {len(ins.notes):4} notes  midi {lo:5.1f}-{hi:5.1f}', flush=True)
        except Exception as e:
            print(f'{n:10} FAILED {e}', flush=True)
