#!/usr/bin/env python3

import json
import sys
import traceback
import wave

import numpy as np
import openvino_genai as ov_genai

RESPONSE_BEGIN_MARKER = "__TRANSCRIPTLAB_OPENVINO_GENAI_RESPONSE_BEGIN__"
RESPONSE_END_MARKER = "__TRANSCRIPTLAB_OPENVINO_GENAI_RESPONSE_END__"


def log(message: str) -> None:
    sys.stderr.write(f"{message}\n")
    sys.stderr.flush()


def sanitize_text(text: str) -> str:
    return " ".join(text.replace("\r", " ").replace("\n", " ").split())


def load_request() -> dict:
    payload = sys.stdin.read()
    if not payload.strip():
        raise RuntimeError("No OpenVINO GenAI worker request was provided.")

    return json.loads(payload)


def load_wave(path: str) -> tuple[np.ndarray, int]:
    with wave.open(path, "rb") as handle:
        channels = handle.getnchannels()
        sample_width = handle.getsampwidth()
        sample_rate = handle.getframerate()
        frame_count = handle.getnframes()
        raw = handle.readframes(frame_count)

    if channels != 1:
        raise RuntimeError(f"OpenVINO GenAI worker expects mono WAV input. channels={channels}")

    if sample_rate != 16000:
        raise RuntimeError(f"OpenVINO GenAI worker expects 16kHz WAV input. sampleRate={sample_rate}")

    if sample_width == 1:
        samples = np.frombuffer(raw, dtype=np.uint8).astype(np.float32)
        samples = (samples - 128.0) / 128.0
    elif sample_width == 2:
        samples = np.frombuffer(raw, dtype=np.int16).astype(np.float32) / 32768.0
    elif sample_width == 4:
        samples = np.frombuffer(raw, dtype=np.int32).astype(np.float32) / 2147483648.0
    else:
        raise RuntimeError(f"Unsupported WAV sample width: {sample_width} bytes")

    duration_ms = int(round(frame_count / sample_rate * 1000.0))
    return samples, duration_ms


def build_generation_config(request: dict):
    config = ov_genai.WhisperGenerationConfig()
    if hasattr(config, "return_timestamps"):
        config.return_timestamps = True
    if hasattr(config, "task"):
        config.task = "transcribe"

    if request.get("languageMode", "").lower() == "fixed":
        language_code = (request.get("languageCode") or "").strip()
        if language_code:
            config.language = f"<|{language_code}|>"

    return config


def build_segments(chunks, duration_ms: int, plain_text: str, log_segments: bool):
    segments = []

    for index, chunk in enumerate(chunks or [], start=1):
        text = sanitize_text(getattr(chunk, "text", "") or "")
        if not text:
            continue

        start_ms = int(round(float(getattr(chunk, "start_ts", 0.0)) * 1000.0))
        end_ms = int(round(float(getattr(chunk, "end_ts", 0.0)) * 1000.0))
        if end_ms < start_ms:
            end_ms = start_ms

        segment = {
            "startMs": start_ms,
            "endMs": end_ms,
            "text": text,
            "speaker": None,
        }
        segments.append(segment)

        if log_segments:
            log(
                f"OpenVINO GenAI segment {index}: startMs={start_ms}, endMs={end_ms}, text={text}"
            )

    if not segments and plain_text:
        segments.append(
            {
                "startMs": 0,
                "endMs": duration_ms,
                "text": plain_text,
                "speaker": None,
            }
        )

    return segments


def build_response(request: dict) -> dict:
    samples, duration_ms = load_wave(request["audioPath"])
    device = (request.get("device") or "GPU").strip() or "GPU"
    log(
        f"OpenVINO GenAI worker loading modelPath={request['modelPath']} device={device} model={request.get('model')}"
    )

    pipeline = ov_genai.WhisperPipeline(request["modelPath"], device)
    generation_config = build_generation_config(request)
    result = pipeline.generate(samples, generation_config=generation_config)

    texts = list(getattr(result, "texts", []) or [])
    plain_text = " ".join(sanitize_text(text) for text in texts if sanitize_text(text)).strip()
    chunks = list(getattr(result, "chunks", []) or [])
    segments = build_segments(chunks, duration_ms, plain_text, bool(request.get("logSegments")))

    if not plain_text:
        plain_text = " ".join(segment["text"] for segment in segments).strip()

    detected_language = None
    if request.get("languageMode", "").lower() == "fixed":
        detected_language = request.get("languageCode")

    return {
        "plainText": plain_text,
        "segments": segments,
        "detectedLanguage": detected_language,
        "durationMs": duration_ms,
    }


def main() -> int:
    try:
        request = load_request()
        response = build_response(request)
        sys.stdout.write(f"{RESPONSE_BEGIN_MARKER}\n")
        sys.stdout.write(json.dumps(response))
        sys.stdout.write(f"\n{RESPONSE_END_MARKER}\n")
        sys.stdout.flush()
        return 0
    except Exception:
        traceback.print_exc(file=sys.stderr)
        sys.stderr.flush()
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
