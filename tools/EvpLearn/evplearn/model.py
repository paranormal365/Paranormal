"""
The model: raw 16 kHz audio in, the probability that a human voice is present out.

Its whole front end — level normalisation, the spectrogram, the noise-floor removal — is inside the network, so the
exported ONNX file takes plain samples and the .NET side on the Windows server has nothing to reimplement or get
subtly wrong.
"""
from __future__ import annotations

import math

import torch
import torch.nn as nn
import torchaudio.functional as AF

RATE = 16_000
N_FFT, WIN, HOP, MELS = 512, 400, 160, 64


class Spectrogram(nn.Module):
    """
    Log-mel spectrogram built from a fixed convolution (one windowed cosine and sine per frequency), not torch.stft:
    the convolution exports to any ONNX runtime unchanged, where STFT support varies.
    """

    def __init__(self):
        super().__init__()
        window = torch.hann_window(WIN, periodic=True)
        n = torch.arange(WIN, dtype=torch.float32)
        bins = torch.arange(N_FFT // 2 + 1, dtype=torch.float32)
        angle = 2 * math.pi * bins[:, None] * n[None, :] / N_FFT
        kernels = torch.cat([window * torch.cos(angle), window * torch.sin(angle)])  # [2F, WIN]
        self.register_buffer("kernels", kernels[:, None, :])
        fbank = AF.melscale_fbanks(N_FFT // 2 + 1, 60.0, 7600.0, MELS, RATE, norm="slaney", mel_scale="slaney")
        self.register_buffer("fbank", fbank.T.contiguous())  # [MELS, F]

    def forward(self, audio: torch.Tensor) -> torch.Tensor:  # [B, S] -> [B, 1, MELS, T]
        # Level-invariant: the clip's peak to 1. Silero VAD scored a quiet voice 0.65 and the same voice 1.00 once raised.
        peak = audio.abs().amax(dim=1, keepdim=True).clamp_min(1e-6)
        x = (audio / peak)[:, None, :]
        y = nn.functional.conv1d(x, self.kernels, stride=HOP)  # [B, 2F, T]
        re, im = y.chunk(2, dim=1)
        power = re * re + im * im
        mel = torch.matmul(self.fbank, power)
        logmel = torch.log(mel + 1e-6)
        # Remove each band's floor (its mean over the clip): what is left is what rose above the room, band by band.
        return (logmel - logmel.mean(dim=2, keepdim=True))[:, None, :, :]


def _block(cin: int, cout: int, pool: bool) -> nn.Sequential:
    layers = [nn.Conv2d(cin, cout, 3, padding=1, bias=False), nn.BatchNorm2d(cout), nn.ReLU(inplace=True)]
    if pool:
        layers.append(nn.MaxPool2d(2))
    return nn.Sequential(*layers)


class VoiceNet(nn.Module):
    def __init__(self):
        super().__init__()
        self.spectrogram = Spectrogram()
        self.body = nn.Sequential(_block(1, 16, True), _block(16, 32, True), _block(32, 64, True), _block(64, 64, False))
        self.head = nn.Linear(64, 1)

    def logits(self, audio: torch.Tensor) -> torch.Tensor:
        z = self.body(self.spectrogram(audio))
        return self.head(z.mean(dim=(2, 3))).squeeze(1)

    def forward(self, audio: torch.Tensor) -> torch.Tensor:
        return torch.sigmoid(self.logits(audio))
