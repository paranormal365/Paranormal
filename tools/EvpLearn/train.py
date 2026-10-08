"""
Item 242 phase 2 — teach VoiceNet to hear a voice buried in noise.

    python train.py --librispeech data/LibriSpeech/test-clean [--backgrounds <folder of real room recordings>]
                    [--epochs 12] [--steps 500] [--batch 64] [--workers 0]

Writes runs/<UTC time>/: model.pt, voicenet.onnx, metrics.json. Runs the same on Windows (keep --workers 0 there unless
you have tried more: Windows starts workers by spawning, which is slower to begin).
"""
from __future__ import annotations

import argparse
import json
import math
import time
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
import onnxruntime as ort
import torch

from evplearn.data import ExampleStream, RealBackgrounds, build_speech_banks, fixed_set
from evplearn.model import VoiceNet

LEVEL_BINS = [(-20, -15), (-15, -10), (-10, -5), (-5, 0), (0, 5), (5, 10)]


def evaluate(model: VoiceNet, examples, batch: int = 256) -> dict:
    model.eval()
    probs = []
    with torch.no_grad():
        for i in range(0, len(examples), batch):
            probs.append(model(torch.from_numpy(np.stack([e.audio for e in examples[i : i + batch]]))).numpy())
    p = np.concatenate(probs)
    voice = np.array([e.voice for e in examples])
    kinds = np.array([e.kind for e in examples])
    levels = np.array([e.level_db for e in examples])
    loss = float(np.mean(-(voice * np.log(np.clip(p, 1e-7, 1)) + (~voice) * np.log(np.clip(1 - p, 1e-7, 1)))))

    def rate(mask, threshold=0.5):
        return float(np.mean(p[mask] >= threshold)) if mask.any() else math.nan

    return {
        "loss": loss,
        "voices_found_by_level": {f"{a:+d}..{b:+d} dB": rate(voice & (levels >= a) & (levels < b)) for a, b in LEVEL_BINS},
        "reversed_voices_found": rate(kinds == "voice-reversed"),
        "impostors_called_voice": rate(kinds == "impostor"),
        "noise_called_voice": rate(kinds == "noise"),
    }


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--librispeech", type=Path, required=True)
    ap.add_argument("--backgrounds", type=Path)
    ap.add_argument("--epochs", type=int, default=12)
    ap.add_argument("--steps", type=int, default=500)
    ap.add_argument("--batch", type=int, default=64)
    ap.add_argument("--workers", type=int, default=0)
    ap.add_argument("--threads", type=int, default=0, help="torch CPU threads (0 = torch's default)")
    ap.add_argument("--validation", type=int, default=4000)
    ap.add_argument("--seed", type=int, default=242)
    args = ap.parse_args()

    if args.threads:
        torch.set_num_threads(args.threads)
    torch.manual_seed(args.seed)
    here = Path(__file__).resolve().parent
    run = here / "runs" / datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S")
    run.mkdir(parents=True)

    t0 = time.time()
    train_speech, val_speech = build_speech_banks(args.librispeech, here / "data" / "cache")
    real = RealBackgrounds(args.backgrounds)
    validation = fixed_set(val_speech, real, args.validation, seed=args.seed + 1)
    print(f"speech crops: {len(train_speech.crops)} train / {len(val_speech.crops)} validation (speaker-disjoint); "
          f"real backgrounds: {len(real.clips)}; ready in {time.time() - t0:.0f}s", flush=True)

    model = VoiceNet()
    loader = torch.utils.data.DataLoader(ExampleStream(train_speech, real, args.seed), batch_size=args.batch, num_workers=args.workers)
    optimiser = torch.optim.AdamW(model.parameters(), lr=2e-3, weight_decay=1e-4)
    schedule = torch.optim.lr_scheduler.OneCycleLR(optimiser, max_lr=2e-3, total_steps=args.epochs * args.steps)
    lossfn = torch.nn.BCEWithLogitsLoss()

    best, history, stream = math.inf, [], iter(loader)
    for epoch in range(1, args.epochs + 1):
        model.train()
        started, running = time.time(), 0.0
        for _ in range(args.steps):
            audio, label = next(stream)
            loss = lossfn(model.logits(audio), label)
            optimiser.zero_grad()
            loss.backward()
            optimiser.step()
            schedule.step()
            running += loss.item()
        metrics = evaluate(model, validation)
        metrics.update(epoch=epoch, train_loss=running / args.steps, seconds=round(time.time() - started, 1))
        history.append(metrics)
        found = ", ".join(f"{k} {v:.0%}" for k, v in metrics["voices_found_by_level"].items())
        print(f"epoch {epoch}: train {metrics['train_loss']:.3f} val {metrics['loss']:.3f} | {found} | reversed {metrics['reversed_voices_found']:.0%} "
              f"impostors {metrics['impostors_called_voice']:.0%} noise {metrics['noise_called_voice']:.0%} | {metrics['seconds']}s", flush=True)
        if metrics["loss"] < best:
            best = metrics["loss"]
            torch.save(model.state_dict(), run / "model.pt")

    model.load_state_dict(torch.load(run / "model.pt"))
    model.eval()
    onnx_path = run / "voicenet.onnx"
    torch.onnx.export(model, torch.zeros(1, 16000), str(onnx_path), input_names=["audio"], output_names=["voice"],
                      dynamic_axes={"audio": {0: "batch", 1: "samples"}, "voice": {0: "batch"}}, opset_version=17, dynamo=False)

    # The exported file must say what the trained model says, or the server would be running a different model.
    check = torch.from_numpy(np.stack([e.audio for e in validation[:32]]))
    session = ort.InferenceSession(str(onnx_path), providers=["CPUExecutionProvider"])
    drift = float(np.max(np.abs(session.run(None, {"audio": check.numpy()})[0] - model(check).detach().numpy())))
    if drift > 1e-4:
        raise SystemExit(f"ONNX export disagrees with the trained model by {drift}")

    (run / "metrics.json").write_text(json.dumps({"best": min(history, key=lambda m: m["loss"]), "history": history,
                                                  "onnx_max_difference": drift, "args": {k: str(v) for k, v in vars(args).items()}}, indent=2))
    print(f"saved {onnx_path} (ONNX matches the trained model within {drift:.1e})")


if __name__ == "__main__":
    main()
