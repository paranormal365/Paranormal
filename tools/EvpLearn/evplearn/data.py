"""
Training examples whose answer is known because they were made: one second of background, sometimes with a real
human voice mixed in at a chosen loudness, sometimes with a sound that is not a voice.

The question the model learns is **"is a human voice present in this second"** — forwards or reversed. A reversed voice
is still a voice; whether it says words is a separate question this model does not answer.
"""
from __future__ import annotations

import math
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import torch

from .audio import RATE, load_mono, mix_at
from .synth import BACKGROUND_KINDS, IMPOSTOR_KINDS, background, impostor

WINDOW = RATE  # one second
LEVELS = (-20.0, 10.0)  # voice-band dB against the background, uniform
VALIDATION_SPEAKERS = 10


@dataclass(frozen=True)
class SpeechBank:
    """Short crops of real speech, int16 to keep memory down. Speakers never cross between train and validation."""
    crops: np.ndarray  # [n, WINDOW] int16

    def take(self, rng: np.random.Generator) -> np.ndarray:
        crop = self.crops[rng.integers(len(self.crops))].astype(np.float32) / 32768
        length = int(rng.uniform(0.3, 1.0) * WINDOW)  # EVPs are short: most voices here are a word or two
        start = rng.integers(0, WINDOW - length + 1)
        return crop[start : start + length]


def build_speech_banks(librispeech_dir: Path, cache_dir: Path, crops_per_file: int = 3) -> tuple[SpeechBank, SpeechBank]:
    """Cuts one-second crops from the loudest parts of each utterance, once, and caches them as .npy."""
    cache_dir.mkdir(parents=True, exist_ok=True)
    train_cache, val_cache = cache_dir / "speech-train.npy", cache_dir / "speech-val.npy"
    if train_cache.exists() and val_cache.exists():
        return SpeechBank(np.load(train_cache)), SpeechBank(np.load(val_cache))

    speakers = sorted(p for p in librispeech_dir.iterdir() if p.is_dir())
    if len(speakers) <= VALIDATION_SPEAKERS:
        raise SystemExit(f"Expected LibriSpeech speaker folders under {librispeech_dir}, found {len(speakers)}.")
    rng = np.random.default_rng(242)
    banks: dict[str, list[np.ndarray]] = {"train": [], "val": []}
    for i, speaker in enumerate(speakers):
        split = "val" if i >= len(speakers) - VALIDATION_SPEAKERS else "train"
        for flac in sorted(speaker.rglob("*.flac")):
            x = load_mono(flac)
            if len(x) < WINDOW:
                continue
            hop = RATE // 4
            starts = np.arange(0, len(x) - WINDOW + 1, hop)
            energy = np.array([np.mean(x[s : s + WINDOW] ** 2) for s in starts])
            loud = starts[energy >= np.median(energy)]
            for s in rng.choice(loud, size=min(crops_per_file, len(loud)), replace=False):
                banks[split].append(np.clip(x[s : s + WINDOW] * 32768, -32768, 32767).astype(np.int16))
    train, val = np.stack(banks["train"]), np.stack(banks["val"])
    np.save(train_cache, train)
    np.save(val_cache, val)
    return SpeechBank(train), SpeechBank(val)


class RealBackgrounds:
    """Ben's own recordings of rooms with nothing happening, cut into random seconds. Optional."""

    def __init__(self, folder: Path | None):
        self.clips = []
        if folder:
            for f in sorted(folder.iterdir()):
                if f.suffix.lower() in (".wav", ".flac") and f.is_file():
                    x = load_mono(f)
                    if len(x) >= WINDOW * 2:
                        self.clips.append(x)

    def take(self, rng: np.random.Generator) -> np.ndarray | None:
        if not self.clips:
            return None
        x = self.clips[rng.integers(len(self.clips))]
        s = rng.integers(0, len(x) - WINDOW + 1)
        return x[s : s + WINDOW].copy()


@dataclass(frozen=True)
class Example:
    audio: np.ndarray
    voice: bool
    level_db: float  # NaN when no voice
    kind: str        # what was placed: voice, voice-reversed, impostor:<kind>, noise


def make_example(rng: np.random.Generator, speech: SpeechBank, real: RealBackgrounds) -> Example:
    bg = real.take(rng) if rng.random() < 0.5 else None
    if bg is None:
        bg = background(BACKGROUND_KINDS[rng.integers(len(BACKGROUND_KINDS))], rng, 1.0)
    x = bg
    kind, level, voice = "noise", math.nan, False

    if rng.random() < 0.5:
        clip = speech.take(rng)
        reversed_ = rng.random() < 0.25
        if reversed_:
            clip = clip[::-1].copy()
        level = float(rng.uniform(*LEVELS))
        x = mix_at(x, clip, int(rng.integers(0, WINDOW - len(clip) + 1)), level)
        kind, voice = ("voice-reversed" if reversed_ else "voice"), True
        if rng.random() < 0.3:
            x = _add_impostor(rng, x)
    elif rng.random() < 0.6:
        x = _add_impostor(rng, x)
        kind = "impostor"

    # What a recorder does to all of it: a low-pass somewhere between phone and hi-fi, and any gain at all.
    if rng.random() < 0.5:
        import torchaudio.functional as AF
        x = AF.lowpass_biquad(torch.from_numpy(x.astype(np.float32)), RATE, float(rng.uniform(3000, 7000))).numpy()
    x = (x * 10 ** (rng.uniform(-30, 20) / 20)).astype(np.float32)
    x = np.clip(x, -1, 1)
    return Example(x, voice, level, kind)


def _add_impostor(rng: np.random.Generator, x: np.ndarray) -> np.ndarray:
    kind = IMPOSTOR_KINDS[rng.integers(len(IMPOSTOR_KINDS))]
    s = impostor(kind, rng)
    if len(s) > WINDOW:
        start = rng.integers(0, len(s) - WINDOW + 1)
        s = s[start : start + WINDOW]
    return mix_at(x, s, int(rng.integers(0, WINDOW - len(s) + 1)), float(rng.uniform(0, 18)))


class ExampleStream(torch.utils.data.IterableDataset):
    """Endless examples. Each DataLoader worker gets its own seed, so workers never repeat each other."""

    def __init__(self, speech: SpeechBank, real: RealBackgrounds, seed: int):
        self.speech, self.real, self.seed = speech, real, seed

    def __iter__(self):
        info = torch.utils.data.get_worker_info()
        rng = np.random.default_rng([self.seed, info.id if info else 0])
        while True:
            e = make_example(rng, self.speech, self.real)
            yield torch.from_numpy(e.audio), torch.tensor(float(e.voice))


def fixed_set(speech: SpeechBank, real: RealBackgrounds, count: int, seed: int) -> list[Example]:
    rng = np.random.default_rng(seed)
    return [make_example(rng, speech, real) for _ in range(count)]
