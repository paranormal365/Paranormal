"""Backgrounds a recorder captures and sounds that fool detectors — the same families as tools/EvpLab/Synth.cs."""
from __future__ import annotations

import numpy as np
import torch
import torchaudio.functional as AF

from .audio import RATE, rms

BACKGROUND_KINDS = ("room", "hiss", "static", "hvac")
IMPOSTOR_KINDS = ("knocks", "footsteps", "creak", "thump")


def _normalise(x: np.ndarray, dbfs: float) -> np.ndarray:
    return (x * (10 ** (dbfs / 20) / max(rms(x), 1e-9))).astype(np.float32)


def _pink(rng: np.random.Generator, n: int) -> np.ndarray:
    white = rng.standard_normal(n)
    spectrum = np.fft.rfft(white)
    f = np.arange(len(spectrum))
    f[0] = 1
    return np.fft.irfft(spectrum / np.sqrt(f), n).astype(np.float32)


def background(kind: str, rng: np.random.Generator, seconds: float, dbfs: float = -45.0) -> np.ndarray:
    n = int(seconds * RATE)
    t = np.arange(n) / RATE
    if kind == "room":
        x = _pink(rng, n)
        x += rms(x) * 0.08 * np.sqrt(2) * np.sin(2 * np.pi * 60 * t + rng.uniform(0, 6.28))
        x *= 10 ** (3 * np.sin(2 * np.pi * rng.uniform(0.05, 0.15) * t + rng.uniform(0, 6.28)) / 20)
    elif kind == "hiss":
        w = rng.standard_normal(n).astype(np.float32)
        x = 0.35 * w + AF.highpass_biquad(torch.from_numpy(w), RATE, 2500.0).numpy()
    elif kind == "static":
        x = _normalise(rng.standard_normal(n), dbfs)
        for _ in range(int(seconds * 6)):
            at = rng.integers(0, max(n - 40, 1))
            amp = rng.uniform(0.02, 0.08) * rng.choice([-1, 1])
            seg = min(40, n - at)
            x[at : at + seg] += amp * np.exp(-np.arange(seg) / 6)
        return x.astype(np.float32)
    elif kind == "hvac":
        x = np.cumsum(rng.standard_normal(n)) * 0.02
        x -= np.convolve(x, np.ones(4001) / 4001, mode="same")
        r = rms(x)
        x += r * 0.15 * np.sqrt(2) * np.sin(2 * np.pi * 120 * t) + r * 0.05 * np.sqrt(2) * np.sin(2 * np.pi * 240 * t)
    else:
        raise ValueError(kind)
    return _normalise(x, dbfs)


def impostor(kind: str, rng: np.random.Generator) -> np.ndarray:
    if kind == "knocks":
        s = np.zeros(int(0.9 * RATE), np.float32)
        f1 = rng.uniform(180, 380)
        tt = np.arange(RATE // 8) / RATE
        hit = np.exp(-tt * 40) * (np.sin(2 * np.pi * f1 * tt) + 0.5 * np.sin(2 * np.pi * f1 * 2.3 * tt))
        for k in range(3):
            at = int(k * 0.28 * RATE)
            s[at : at + len(hit)] += hit[: len(s) - at]
        return s
    if kind == "footsteps":
        s = np.zeros(int(2.2 * RATE), np.float32)
        for k in range(4):
            at = int(k * 0.55 * RATE)
            burst = rng.uniform(-1, 1, RATE // 10) * np.exp(-np.arange(RATE // 10) / 400)
            s[at : at + len(burst)] += AF.lowpass_biquad(torch.from_numpy(burst.astype(np.float32)), RATE, 250.0).numpy()
        return s
    if kind == "creak":
        n = int(rng.uniform(0.5, 1.1) * RATE)
        u = np.arange(n) / n
        f = rng.uniform(350, 600) * (1 + 0.6 * u) + 20 * np.sin(2 * np.pi * 9 * u)
        phase = np.cumsum(2 * np.pi * f / RATE)
        return (np.sin(np.pi * u) * (np.sin(phase) + 0.4 * np.sin(2 * phase) + 0.2 * np.sin(3 * phase))).astype(np.float32)
    if kind == "thump":
        n = int(0.6 * RATE)
        tt = np.arange(n) / RATE
        noise = AF.lowpass_biquad(torch.from_numpy((rng.uniform(-1, 1, n) * np.exp(-tt * 18)).astype(np.float32)), RATE, 900.0).numpy()
        return (noise + 0.6 * np.exp(-tt * 10) * np.sin(2 * np.pi * 70 * tt)).astype(np.float32)
    raise ValueError(kind)
