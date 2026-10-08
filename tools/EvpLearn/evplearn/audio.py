"""Samples in, samples out: 16 kHz mono float32, the rate everything here works at."""
from __future__ import annotations

import math
from pathlib import Path

import numpy as np
import soundfile as sf
import torch
import torchaudio.functional as AF

RATE = 16_000


def load_mono(path: Path | str) -> np.ndarray:
    """Any WAV/FLAC soundfile reads, down-mixed and resampled to 16 kHz. Paths work the same on Windows."""
    data, rate = sf.read(str(path), dtype="float32", always_2d=True)
    mono = data.mean(axis=1)
    if rate != RATE:
        mono = AF.resample(torch.from_numpy(mono), rate, RATE).numpy()
    return mono.astype(np.float32)


def rms(x: np.ndarray) -> float:
    return float(np.sqrt(np.mean(np.square(x, dtype=np.float64)))) if x.size else 0.0


def voice_band(x: np.ndarray) -> np.ndarray:
    """300–3400 Hz, the band a voice's intelligibility lives in — where loudness is measured against the noise."""
    t = torch.from_numpy(np.ascontiguousarray(x))
    t = AF.highpass_biquad(t, RATE, 300.0)
    t = AF.lowpass_biquad(t, RATE, 3400.0)
    return t.numpy()


def active_rms(x: np.ndarray) -> float:
    """RMS over 20 ms frames within 30 dB of the loudest: a phrase's gaps would otherwise make it seem quieter."""
    frame = RATE // 50
    n = len(x) // frame
    if n == 0:
        return rms(x)
    frames = np.sqrt(np.mean(np.square(x[: n * frame].reshape(n, frame), dtype=np.float64), axis=1))
    peak = frames.max()
    if peak <= 0:
        return 0.0
    active = frames[frames > peak * 10 ** (-30 / 20)]
    return float(np.sqrt(np.mean(active**2)))


def mix_at(background: np.ndarray, sound: np.ndarray, at: int, level_db: float) -> np.ndarray:
    """Adds `sound` at sample `at` so its voice-band active RMS sits `level_db` above the background's there."""
    out = background.copy()
    n = min(len(sound), len(out) - at)
    if n <= 0:
        return out
    bg = rms(voice_band(out[at : at + n]))
    fg = active_rms(voice_band(sound[:n]))
    if fg > 0 and bg > 0:
        out[at : at + n] += sound[:n] * (bg * 10 ** (level_db / 20) / fg)
    return out


def db(x: float) -> float:
    return 20 * math.log10(max(x, 1e-9))
