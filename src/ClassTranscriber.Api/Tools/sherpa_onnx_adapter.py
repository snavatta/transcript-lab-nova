#!/usr/bin/env python3
import argparse
import json
import math
import sys
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="SherpaOnnx adapter for TranscriptLab Nova")
    parser.add_argument("--model-dir", required=True)
    parser.add_argument("--input-wav", required=True)
    parser.add_argument("--output-json", required=True)
    parser.add_argument("--language", default="auto")
    parser.add_argument("--provider", default="cpu")
    parser.add_argument("--num-threads", type=int, default=4)
    return parser.parse_args()


def load_dependencies():
    try:
        import numpy as np  # type: ignore
        import sherpa_onnx  # type: ignore
        import soundfile as sf  # type: ignore
        return np, sherpa_onnx, sf
    except Exception as ex:  # pragma: no cover - runtime dependency guard
        raise RuntimeError(
            "Missing SherpaOnnx Python runtime. Install with: python3 -m pip install --user sherpa-onnx soundfile numpy"
        ) from ex


def load_model_config(model_dir: Path) -> dict:
    config_path = model_dir / "config.json"
    if config_path.exists():
        return json.loads(config_path.read_text(encoding="utf-8"))

    if (model_dir / "model.onnx").exists() and (model_dir / "tokens.txt").exists():
        return {
            "backend": "sense_voice",
            "model": "model.onnx",
            "tokens": "tokens.txt",
            "use_itn": True,
        }

    if (model_dir / "encoder.onnx").exists() and (model_dir / "decoder.onnx").exists() and (model_dir / "tokens.txt").exists():
        return {
            "backend": "whisper",
            "encoder": "encoder.onnx",
            "decoder": "decoder.onnx",
            "tokens": "tokens.txt",
            "task": "transcribe",
        }

    raise RuntimeError(
        f"No Sherpa model configuration found in {model_dir}. Add config.json or provide the expected default files."
    )


def create_recognizer(sherpa_onnx, model_dir: Path, config: dict, language: str, provider: str, num_threads: int):
    backend = str(config.get("backend", "sense_voice")).lower()

    if backend == "sense_voice":
        return sherpa_onnx.OfflineRecognizer.from_sense_voice(
            model=str(model_dir / config.get("model", "model.onnx")),
            tokens=str(model_dir / config.get("tokens", "tokens.txt")),
            num_threads=num_threads,
            use_itn=bool(config.get("use_itn", True)),
            language=language,
            provider=provider,
        )

    if backend == "whisper":
        # sherpa-onnx whisper backend uses empty string for auto-detection
        whisper_language = "" if language.lower() == "auto" else language
        return sherpa_onnx.OfflineRecognizer.from_whisper(
            encoder=str(model_dir / config.get("encoder", "encoder.onnx")),
            decoder=str(model_dir / config.get("decoder", "decoder.onnx")),
            tokens=str(model_dir / config.get("tokens", "tokens.txt")),
            language=whisper_language,
            task=str(config.get("task", "transcribe")),
            num_threads=num_threads,
            provider=provider,
        )

    raise RuntimeError(f"Unsupported Sherpa backend '{backend}'.")


def normalize_audio(np, sf, input_wav: Path):
    audio, sample_rate = sf.read(str(input_wav), dtype="float32")
    if len(audio.shape) > 1:
        audio = audio[:, 0]
    duration_ms = int(round((len(audio) / sample_rate) * 1000)) if sample_rate else 0
    return np.asarray(audio, dtype="float32"), int(sample_rate), duration_ms


def build_segments(text: str, timestamps, duration_ms: int):
    normalized_text = text.strip()
    if not normalized_text:
        return []

    words = normalized_text.split()
    times = list(timestamps or [])

    if times and len(words) > 0:
        millis = [int(round(float(value) * 1000)) for value in times]
        segments = []

        if len(millis) == len(words) + 1:
            for index, word in enumerate(words):
                start_ms = millis[index]
                end_ms = millis[index + 1]
                segments.append({"startMs": start_ms, "endMs": max(end_ms, start_ms), "text": word, "speaker": None})
            return segments

        if len(millis) == len(words):
            for index, word in enumerate(words):
                start_ms = millis[index]
                if index + 1 < len(millis):
                    end_ms = millis[index + 1]
                else:
                    end_ms = duration_ms or start_ms
                segments.append({"startMs": start_ms, "endMs": max(end_ms, start_ms), "text": word, "speaker": None})
            return segments

    return [{"startMs": 0, "endMs": duration_ms, "text": normalized_text, "speaker": None}]


def main() -> int:
    args = parse_args()
    np, sherpa_onnx, sf = load_dependencies()
    model_dir = Path(args.model_dir)
    input_wav = Path(args.input_wav)
    output_json = Path(args.output_json)

    config = load_model_config(model_dir)
    recognizer = create_recognizer(
        sherpa_onnx=sherpa_onnx,
        model_dir=model_dir,
        config=config,
        language=args.language,
        provider=args.provider,
        num_threads=args.num_threads,
    )

    audio, sample_rate, duration_ms = normalize_audio(np, sf, input_wav)
    stream = recognizer.create_stream()
    stream.accept_waveform(sample_rate, audio)
    recognizer.decode_stream(stream)
    result = stream.result

    text = getattr(result, "text", "") or ""
    timestamps = getattr(result, "timestamps", None)
    segments = build_segments(text, timestamps, duration_ms)
    detected_language = getattr(result, "language", None)

    payload = {
        "text": text.strip(),
        "detectedLanguage": detected_language,
        "durationMs": duration_ms,
        "segments": segments,
    }

    output_json.parent.mkdir(parents=True, exist_ok=True)
    output_json.write_text(json.dumps(payload), encoding="utf-8")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as ex:  # pragma: no cover - process boundary error handling
        print(str(ex), file=sys.stderr)
        raise SystemExit(1)