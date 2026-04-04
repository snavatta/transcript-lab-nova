#!/usr/bin/env python3
"""
OpenVINO Whisper FastAPI sidecar.

Long-running HTTP server that loads Whisper models via openvino_genai and keeps
them cached in memory between transcription requests.  Spawned once per engine
lifetime by the ClassTranscriber API and communicated with over localhost HTTP.

Usage:
    python3 openvino_whisper_sidecar.py [--port PORT] [--host HOST]
        [--models-path PATH] [--model-download-base-url URL] [--log-segments]
"""

import argparse
import asyncio
import io
import json
import os
import shutil
import sys
import threading
import time
import wave
from pathlib import Path
from threading import Lock
from typing import AsyncGenerator

import numpy as np
import openvino as ov
import openvino_genai as ov_genai
import uvicorn
from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from fastapi.responses import StreamingResponse
from pydantic import BaseModel

# ---------------------------------------------------------------------------
# Globals (set at startup by _configure)
# ---------------------------------------------------------------------------

_models_path: str = "./data/models/openvino-genai"
_model_download_base_url: str = "https://huggingface.co"
_log_segments_default: bool = False

# pipeline cache: keyed by "{model_path}::{device}"
_pipeline_cache: dict[str, ov_genai.WhisperPipeline] = {}
_pipeline_loading: dict[str, threading.Event] = {}  # tracks in-progress loads
_cache_lock = Lock()

# download progress broadcast: keyed by model name, value list of asyncio queues
_download_listeners: dict[str, list] = {}
_download_lock = Lock()


# ---------------------------------------------------------------------------
# Logging
# ---------------------------------------------------------------------------

def log(message: str) -> None:
    sys.stderr.write(f"{message}\n")
    sys.stderr.flush()


# ---------------------------------------------------------------------------
# Model catalog
# ---------------------------------------------------------------------------

# INT8 models include openvino_config.json (quantization parameters).
# FP16 models do not publish that file.
_COMMON_REQUIRED_FILES_INT8 = [
    "config.json",
    "generation_config.json",
    "openvino_config.json",
    "openvino_encoder_model.xml",
    "openvino_encoder_model.bin",
    "openvino_decoder_model.xml",
    "openvino_decoder_model.bin",
    "openvino_tokenizer.xml",
    "openvino_tokenizer.bin",
    "openvino_detokenizer.xml",
    "openvino_detokenizer.bin",
    "preprocessor_config.json",
    "tokenizer.json",
    "tokenizer_config.json",
    "special_tokens_map.json",
    "merges.txt",
    "vocab.json",
]

_COMMON_REQUIRED_FILES_FP16 = [
    "config.json",
    "generation_config.json",
    "openvino_encoder_model.xml",
    "openvino_encoder_model.bin",
    "openvino_decoder_model.xml",
    "openvino_decoder_model.bin",
    "openvino_tokenizer.xml",
    "openvino_tokenizer.bin",
    "openvino_detokenizer.xml",
    "openvino_detokenizer.bin",
    "preprocessor_config.json",
    "tokenizer.json",
    "tokenizer_config.json",
    "special_tokens_map.json",
    "merges.txt",
    "vocab.json",
]

# Two-decoder non-stateful format (exported with --disable-stateful): no openvino_config.json,
# has separate openvino_decoder_with_past_model.* for subsequent decode steps.
_COMMON_REQUIRED_FILES_WITH_PAST = [
    "config.json",
    "generation_config.json",
    "openvino_encoder_model.xml",
    "openvino_encoder_model.bin",
    "openvino_decoder_model.xml",
    "openvino_decoder_model.bin",
    "openvino_decoder_with_past_model.xml",
    "openvino_decoder_with_past_model.bin",
    "openvino_tokenizer.xml",
    "openvino_tokenizer.bin",
    "openvino_detokenizer.xml",
    "openvino_detokenizer.bin",
    "preprocessor_config.json",
    "tokenizer.json",
    "tokenizer_config.json",
    "special_tokens_map.json",
    "merges.txt",
    "vocab.json",
]

_CATALOG: dict[str, dict] = {
    "tiny-int8": {
        "display_name": "Whisper Tiny INT8",
        "repository": "OpenVINO/whisper-tiny-int8-ov",
        "required_files": _COMMON_REQUIRED_FILES_INT8,
    },
    "tiny-fp16": {
        "display_name": "Whisper Tiny FP16",
        "repository": "OpenVINO/whisper-tiny-fp16-ov",
        "required_files": _COMMON_REQUIRED_FILES_FP16,
    },
    "base-int8": {
        "display_name": "Whisper Base INT8",
        "repository": "OpenVINO/whisper-base-int8-ov",
        "required_files": _COMMON_REQUIRED_FILES_INT8,
    },
    "base-fp16": {
        "display_name": "Whisper Base FP16",
        "repository": "OpenVINO/whisper-base-fp16-ov",
        "required_files": _COMMON_REQUIRED_FILES_FP16,
    },
    "small-int8": {
        "display_name": "Whisper Small INT8",
        "repository": "OpenVINO/whisper-small-int8-ov",
        "required_files": _COMMON_REQUIRED_FILES_INT8,
    },
    "small-fp16": {
        "display_name": "Whisper Small FP16",
        "repository": "OpenVINO/whisper-small-fp16-ov",
        "required_files": _COMMON_REQUIRED_FILES_FP16,
    },
    "medium-int8": {
        "display_name": "Whisper Medium INT8",
        "repository": "OpenVINO/whisper-medium-int8-ov",
        "required_files": _COMMON_REQUIRED_FILES_INT8,
    },
    "medium-fp16": {
        "display_name": "Whisper Medium FP16",
        "repository": "OpenVINO/whisper-medium-fp16-ov",
        "required_files": _COMMON_REQUIRED_FILES_FP16,
    },
    "large-v3-int8": {
        "display_name": "Whisper Large-V3 INT8",
        "repository": "OpenVINO/whisper-large-v3-int8-ov",
        "required_files": _COMMON_REQUIRED_FILES_INT8,
    },
    "large-v3-fp16": {
        "display_name": "Whisper Large-V3 FP16",
        "repository": "OpenVINO/whisper-large-v3-fp16-ov",
        "required_files": _COMMON_REQUIRED_FILES_FP16,
    },
}


def _model_dir(model_name: str) -> Path:
    return Path(_models_path) / model_name


def _is_installed(model_name: str) -> bool:
    if model_name not in _CATALOG:
        return False
    model_path = _model_dir(model_name)
    if not model_path.is_dir():
        return False
    for required_file in _CATALOG[model_name]["required_files"]:
        if not (model_path / required_file).exists():
            return False
    return True


def _model_size_bytes(model_name: str) -> int | None:
    model_path = _model_dir(model_name)
    if not model_path.is_dir():
        return None
    total = 0
    for f in model_path.rglob("*"):
        if f.is_file():
            try:
                total += f.stat().st_size
            except OSError:
                pass
    return total


# ---------------------------------------------------------------------------
# Device resolution shared by the OpenVINO Whisper integration paths
# ---------------------------------------------------------------------------

def _resolve_device(requested_device: str) -> str:
    core = ov.Core()
    available = [str(d) for d in list(getattr(core, "available_devices", []) or [])]
    normalized = (requested_device.strip() or "GPU").upper()

    if normalized == "AUTO":
        gpu_devices = [d for d in available if d.upper() == "GPU" or d.upper().startswith("GPU.")]
        resolved = "AUTO:GPU,CPU" if gpu_devices else "AUTO:CPU"
        log(
            f"OpenVINO sidecar resolved requestedDevice={requested_device} to device={resolved} "
            f"availableDevices={available}"
        )
        return resolved

    if normalized == "GPU":
        gpu_devices = [d for d in available if d.upper() == "GPU" or d.upper().startswith("GPU.")]
        if not gpu_devices:
            raise RuntimeError(
                f"OpenVINO sidecar could not find a usable GPU device for requested device "
                f"'{requested_device}'. availableDevices={available}"
            )
        indexed = [d for d in gpu_devices if "." in d]
        resolved = indexed[0] if indexed else gpu_devices[0]
    else:
        resolved = requested_device
        if ":" not in resolved and "," not in resolved and not any(
            d.upper() == normalized for d in available
        ):
            raise RuntimeError(
                f"OpenVINO sidecar could not find requested device '{requested_device}'. "
                f"availableDevices={available}"
            )

    if ":" not in resolved and "," not in resolved:
        try:
            full_name = core.get_property(resolved, "FULL_DEVICE_NAME")
            log(
                f"OpenVINO sidecar resolved requestedDevice={requested_device} to "
                f"device={resolved} fullDeviceName={full_name}"
            )
        except Exception:
            pass

    return resolved


# ---------------------------------------------------------------------------
# Pipeline cache
# ---------------------------------------------------------------------------

def _get_or_load_pipeline(model_path: str, device: str) -> ov_genai.WhisperPipeline:
    cache_key = f"{model_path}::{device}"
    while True:
        with _cache_lock:
            if cache_key in _pipeline_cache:
                return _pipeline_cache[cache_key]
            if cache_key not in _pipeline_loading:
                # This thread claims the load slot
                loading_event = threading.Event()
                _pipeline_loading[cache_key] = loading_event
                break
            loading_event = _pipeline_loading[cache_key]
        # Another thread is loading this model; wait outside the lock to avoid blocking
        loading_event.wait(timeout=600.0)
        # Re-check: model may now be in cache (or load may have failed → retry)

    # Only one thread reaches here per cache_key at a time
    try:
        log(f"OpenVINO sidecar: loading model from {model_path} on device {device}")
        pipeline = _load_pipeline_with_retry(model_path, device)
        log(f"OpenVINO sidecar: model loaded from {model_path}")
        with _cache_lock:
            _pipeline_cache[cache_key] = pipeline
        return pipeline
    finally:
        # Signal any waiting threads whether load succeeded or failed
        with _cache_lock:
            event = _pipeline_loading.pop(cache_key, None)
        if event is not None:
            event.set()


def _load_pipeline_with_retry(model_path: str, device: str, retries: int = 2, delay: float = 2.0) -> ov_genai.WhisperPipeline:
    """Load an ov_genai.WhisperPipeline, retrying on broken-pipe errors (transient GPU compilation subprocess crash).

    OpenVINO wraps the underlying OS broken-pipe signal as a RuntimeError whose message contains
    '[Errno 32] Broken pipe', rather than raising a Python BrokenPipeError directly.
    """
    last_exc: Exception | None = None
    for attempt in range(retries):
        try:
            return ov_genai.WhisperPipeline(model_path, device)
        except Exception as exc:
            if not _is_broken_pipe_error(exc):
                raise
            last_exc = exc
            log(
                f"OpenVINO sidecar: model load attempt {attempt + 1}/{retries} failed with broken pipe"
                f" (device={device}); retrying in {delay}s"
            )
            time.sleep(delay)
    raise last_exc  # type: ignore[misc]


def _is_broken_pipe_error(exc: Exception) -> bool:
    """Return True when exc is, or wraps, a broken-pipe signal from the GPU compilation subprocess."""
    if isinstance(exc, BrokenPipeError):
        return True
    # OpenVINO's C++ frontend raises RuntimeError with '[Errno 32] Broken pipe' in the message.
    return isinstance(exc, RuntimeError) and ("[Errno 32]" in str(exc) or "Broken pipe" in str(exc))


def _is_pipeline_loaded(model_path: str) -> bool:
    with _cache_lock:
        return any(k.startswith(f"{model_path}::") for k in _pipeline_cache)


def _get_available_devices() -> list[str]:
    try:
        core = ov.Core()
        return [str(d) for d in list(getattr(core, "available_devices", []) or [])]
    except Exception:
        return []


def _format_model_load_error(exc: Exception, model_path: str, requested_device: str, resolved_device: str) -> str:
    raw_detail = str(exc).strip()
    compact_detail = " ".join(line.strip() for line in raw_detail.splitlines() if line.strip())
    available = _get_available_devices()
    message = (
        f"Failed to load OpenVINO Whisper model from '{model_path}' on requested device "
        f"'{requested_device}' (resolved to '{resolved_device}')."
    )

    if _is_gpu_program_build_failure(compact_detail):
        suggestions = [
            "Intel GPU model compilation failed.",
            "This usually means the selected GPU/runtime stack cannot compile this model as configured.",
            "Try device=CPU to confirm the model assets are valid.",
            "If the host has multiple Intel GPU devices, try an explicit device like GPU.0 or GPU.1.",
            "If the model is INT8, try an FP16 variant such as base-fp16 or small-fp16.",
        ]
        if available:
            suggestions.append(f"Available devices: {available}.")
        suggestions.append(f"OpenVINO detail: {compact_detail}")
        return f"{message} {' '.join(suggestions)}"

    if available:
        return f"{message} Available devices: {available}. OpenVINO detail: {compact_detail}"

    return f"{message} OpenVINO detail: {compact_detail}"


def _is_gpu_program_build_failure(detail: str) -> bool:
    normalized = detail.lower()
    return (
        "programbuilder build failed" in normalized
        or "program build failed" in normalized
        or "src/plugins/intel_gpu" in normalized
        or "[gpu]" in normalized
    )


# ---------------------------------------------------------------------------
# WAV loading
# ---------------------------------------------------------------------------

def _read_wave_handle(handle) -> tuple[np.ndarray, int]:
    channels = handle.getnchannels()
    sample_width = handle.getsampwidth()
    sample_rate = handle.getframerate()
    n_frames = handle.getnframes()
    raw = handle.readframes(n_frames)

    if channels != 1:
        raise ValueError(f"Expected mono WAV input, got {channels} channels")
    if sample_rate != 16000:
        raise ValueError(f"Expected 16 kHz WAV input, got {sample_rate} Hz")

    if sample_width == 1:
        samples = (np.frombuffer(raw, dtype=np.uint8).astype(np.float32) - 128.0) / 128.0
    elif sample_width == 2:
        samples = np.frombuffer(raw, dtype=np.int16).astype(np.float32) / 32768.0
    elif sample_width == 4:
        samples = np.frombuffer(raw, dtype=np.int32).astype(np.float32) / 2147483648.0
    else:
        raise ValueError(f"Unsupported WAV sample width: {sample_width} bytes")

    duration_ms = int(round(n_frames / sample_rate * 1000.0))
    return samples, duration_ms


def _load_wave_path(path: str) -> tuple[np.ndarray, int]:
    with wave.open(path, "rb") as handle:
        return _read_wave_handle(handle)


def _load_wave_bytes(data: bytes) -> tuple[np.ndarray, int]:
    with wave.open(io.BytesIO(data), "rb") as handle:
        return _read_wave_handle(handle)


# ---------------------------------------------------------------------------
# Generation config and result parsing
# ---------------------------------------------------------------------------

def _build_generation_config(
    pipeline: ov_genai.WhisperPipeline,
    language_mode: str,
    language_code: str | None,
) -> ov_genai.WhisperGenerationConfig:
    # Must use pipeline.get_generation_config() to get a properly initialized config.
    # ov_genai.WhisperGenerationConfig() produces an empty object with uninitialized
    # token lists that causes a C++ vector::reserve crash in generate() (openvino_genai 2026.x).
    config = pipeline.get_generation_config()
    if hasattr(config, "return_timestamps"):
        config.return_timestamps = True
    if hasattr(config, "task"):
        config.task = "transcribe"
    if language_mode.lower() == "fixed":
        code = (language_code or "").strip()
        if code:
            config.language = f"<|{code}|>"
    return config


def _sanitize_text(text: str) -> str:
    return " ".join(text.replace("\r", " ").replace("\n", " ").split())


def _build_segments(chunks, duration_ms: int, plain_text: str, log_segs: bool) -> list[dict]:
    """Build segment dicts from openvino_genai result chunks."""
    segments: list[dict] = []
    for index, chunk in enumerate(chunks or [], start=1):
        # Use start_ts / end_ts attributes from the OpenVINO Whisper pipeline output
        text = _sanitize_text(getattr(chunk, "text", "") or "")
        if not text:
            continue
        start_ms = int(round(float(getattr(chunk, "start_ts", 0.0)) * 1000.0))
        end_ms = int(round(float(getattr(chunk, "end_ts", 0.0)) * 1000.0))
        if end_ms < start_ms:
            end_ms = start_ms
        segments.append({"start_ms": start_ms, "end_ms": end_ms, "text": text, "speaker": None})
        if log_segs:
            log(f"OpenVINO sidecar segment {index}: start={start_ms}ms end={end_ms}ms text={text[:80]}")
    if not segments and plain_text:
        segments.append({"start_ms": 0, "end_ms": duration_ms, "text": plain_text, "speaker": None})
    return segments


def _extract_plain_text(result) -> str:
    """Extract plain text from result.texts list first, fall back to empty string."""
    texts = list(getattr(result, "texts", []) or [])
    return " ".join(_sanitize_text(t) for t in texts if _sanitize_text(t)).strip()


def _extract_detected_language(result, language_mode: str, language_code: str | None) -> str | None:
    if language_mode.lower() == "fixed":
        return language_code
    lang = getattr(result, "language", None)
    if isinstance(lang, str) and lang:
        return lang.strip("<>|")
    return None


# ---------------------------------------------------------------------------
# HuggingFace model download
# ---------------------------------------------------------------------------

def _hf_file_url(repository: str, filename: str) -> str:
    return f"{_model_download_base_url}/{repository}/resolve/main/{filename}"


def _download_model(model_name: str, progress_callback) -> None:
    """Download all required files for a catalog model.
    progress_callback(progress: float, bytes_downloaded: int, bytes_total: int) is called periodically.
    """
    import urllib.request

    catalog_entry = _CATALOG[model_name]
    repository = catalog_entry["repository"]
    required_files: list[str] = catalog_entry["required_files"]
    model_path = _model_dir(model_name)
    model_path.mkdir(parents=True, exist_ok=True)

    total_files = len(required_files)
    for file_index, filename in enumerate(required_files):
        dest = model_path / filename
        if dest.exists():
            progress_callback((file_index + 1) / total_files, 0, 0)
            continue

        url = _hf_file_url(repository, filename)
        log(f"OpenVINO sidecar: downloading {url}")
        tmp_dest = model_path / f"{filename}.download"
        try:
            with urllib.request.urlopen(url, timeout=300) as response:
                content_length = response.headers.get("Content-Length")
                file_total = int(content_length) if content_length else 0
                downloaded = 0
                with open(tmp_dest, "wb") as out_file:
                    while True:
                        chunk = response.read(65536)
                        if not chunk:
                            break
                        out_file.write(chunk)
                        downloaded += len(chunk)
                        if file_total > 0:
                            file_frac = downloaded / file_total
                        else:
                            file_frac = 0.5
                        overall = (file_index + file_frac) / total_files
                        progress_callback(overall, downloaded, file_total)
            tmp_dest.rename(dest)
        except Exception:
            if tmp_dest.exists():
                tmp_dest.unlink()
            raise

        progress_callback((file_index + 1) / total_files, 0, 0)


# ---------------------------------------------------------------------------
# Pydantic models
# ---------------------------------------------------------------------------

class TranscribeRequest(BaseModel):
    audio_path: str
    model_path: str
    device: str = "GPU"
    language_mode: str = "Auto"
    language_code: str | None = None
    log_segments: bool = False


class TranscriptSegment(BaseModel):
    start_ms: int
    end_ms: int
    text: str
    speaker: str | None = None


class TranscribeResponse(BaseModel):
    plain_text: str
    segments: list[TranscriptSegment]
    detected_language: str | None
    duration_ms: int


class ModelDownloadRequest(BaseModel):
    model: str


class ModelStatusEntry(BaseModel):
    name: str
    display_name: str
    is_installed: bool
    install_path: str | None
    size_bytes: int | None


class ModelsListResponse(BaseModel):
    models: list[ModelStatusEntry]


class OpenAiModelEntry(BaseModel):
    id: str
    object: str = "model"
    created: int = 0
    owned_by: str = "local"


class OpenAiModelsListResponse(BaseModel):
    object: str = "list"
    data: list[OpenAiModelEntry]


class OpenAiSegment(BaseModel):
    id: int
    seek: int = 0
    start: float
    end: float
    text: str
    tokens: list[int] = []
    temperature: float = 0.0
    avg_logprob: float = 0.0
    compression_ratio: float = 1.0
    no_speech_prob: float = 0.0


class OpenAiTranscriptionResponse(BaseModel):
    task: str = "transcribe"
    language: str | None
    duration: float
    text: str
    segments: list[OpenAiSegment]


class DeviceEntry(BaseModel):
    id: str
    name: str


class DevicesResponse(BaseModel):
    devices: list[DeviceEntry]


# ---------------------------------------------------------------------------
# FastAPI app
# ---------------------------------------------------------------------------

app = FastAPI(title="OpenVINO Whisper Sidecar", version="2.0")


@app.get("/health")
def health() -> dict:
    return {"status": "ok"}


@app.get("/devices", response_model=DevicesResponse)
def get_devices() -> DevicesResponse:
    try:
        core = ov.Core()
        available = _get_available_devices()
        entries: list[DeviceEntry] = []
        for device_id in available:
            try:
                name = str(core.get_property(device_id, "FULL_DEVICE_NAME"))
            except Exception:
                name = device_id
            entries.append(DeviceEntry(id=device_id, name=name))
        return DevicesResponse(devices=entries)
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"Failed to query devices: {exc}") from exc


@app.get("/models", response_model=ModelsListResponse)
def list_models() -> ModelsListResponse:
    entries: list[ModelStatusEntry] = []
    for name, catalog_entry in _CATALOG.items():
        installed = _is_installed(name)
        install_path = str(_model_dir(name)) if installed else None
        size = _model_size_bytes(name) if installed else None
        entries.append(ModelStatusEntry(
            name=name,
            display_name=catalog_entry["display_name"],
            is_installed=installed,
            install_path=install_path,
            size_bytes=size,
        ))
    return ModelsListResponse(models=entries)


@app.get("/v1/models", response_model=OpenAiModelsListResponse)
def list_models_openai() -> OpenAiModelsListResponse:
    installed = [OpenAiModelEntry(id=name) for name in _CATALOG if _is_installed(name)]
    return OpenAiModelsListResponse(data=installed)


@app.post("/models/download")
async def download_model(req: ModelDownloadRequest) -> StreamingResponse:
    model_name = req.model
    if model_name not in _CATALOG:
        raise HTTPException(status_code=404, detail=f"Model '{model_name}' is not in the catalog.")

    async def event_stream() -> AsyncGenerator[str, None]:
        loop = asyncio.get_event_loop()
        queue: asyncio.Queue = asyncio.Queue()

        with _download_lock:
            already_running = model_name in _download_listeners
            if model_name not in _download_listeners:
                _download_listeners[model_name] = []
            _download_listeners[model_name].append(queue)

        yield f"data: {json.dumps({'status': 'starting', 'model': model_name, 'progress': 0.0})}\n\n"

        if not already_running:
            def run_download():
                def on_progress(progress: float, bytes_downloaded: int, bytes_total: int):
                    event = {
                        "status": "downloading",
                        "model": model_name,
                        "progress": round(progress, 4),
                        "bytes_downloaded": bytes_downloaded,
                        "bytes_total": bytes_total,
                    }
                    with _download_lock:
                        listeners = list(_download_listeners.get(model_name, []))
                    for q in listeners:
                        loop.call_soon_threadsafe(q.put_nowait, ("progress", event))

                try:
                    _download_model(model_name, on_progress)
                    done_event = {"status": "complete", "model": model_name, "progress": 1.0}
                    with _download_lock:
                        listeners = list(_download_listeners.pop(model_name, []))
                    for q in listeners:
                        loop.call_soon_threadsafe(q.put_nowait, ("complete", done_event))
                except Exception as exc:
                    err_event = {"status": "error", "model": model_name, "error": str(exc)}
                    log(f"OpenVINO sidecar: model download error for {model_name}: {exc}")
                    with _download_lock:
                        listeners = list(_download_listeners.pop(model_name, []))
                    for q in listeners:
                        loop.call_soon_threadsafe(q.put_nowait, ("error", err_event))

            t = threading.Thread(target=run_download, daemon=True)
            t.start()

        while True:
            try:
                kind, event = await asyncio.wait_for(queue.get(), timeout=30.0)
                yield f"data: {json.dumps(event)}\n\n"
                if kind in ("complete", "error"):
                    break
            except asyncio.TimeoutError:
                yield f"data: {json.dumps({'status': 'heartbeat', 'model': model_name})}\n\n"

    return StreamingResponse(event_stream(), media_type="text/event-stream")


@app.delete("/models/{model_name}", status_code=204)
def delete_model(model_name: str) -> None:
    if model_name not in _CATALOG:
        raise HTTPException(status_code=404, detail=f"Model '{model_name}' is not in the catalog.")
    model_path = _model_dir(model_name)
    if not model_path.is_dir():
        raise HTTPException(status_code=404, detail=f"Model '{model_name}' is not installed.")
    if _is_pipeline_loaded(str(model_path)):
        raise HTTPException(
            status_code=409,
            detail=f"Model '{model_name}' is currently loaded in the pipeline cache and cannot be deleted.",
        )
    try:
        shutil.rmtree(model_path)
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"Failed to delete model: {exc}") from exc


@app.post("/transcribe", response_model=TranscribeResponse)
def transcribe(req: TranscribeRequest) -> TranscribeResponse:
    """Internal transcription endpoint (file path based, used by ClassTranscriber .NET engine)."""
    try:
        resolved_device = _resolve_device(req.device)
    except RuntimeError as exc:
        raise HTTPException(status_code=503, detail=str(exc)) from exc

    try:
        pipeline = _get_or_load_pipeline(req.model_path, resolved_device)
    except Exception as exc:
        raise HTTPException(
            status_code=503,
            detail=_format_model_load_error(exc, req.model_path, req.device, resolved_device),
        ) from exc

    try:
        samples, duration_ms = _load_wave_path(req.audio_path)
    except (ValueError, Exception) as exc:
        raise HTTPException(status_code=400, detail=f"Failed to read audio: {exc}") from exc

    config = _build_generation_config(pipeline, req.language_mode, req.language_code)

    try:
        # Use generation_config as keyword argument (required by openvino_genai API)
        result = pipeline.generate(samples, generation_config=config)
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"Transcription failed: {exc}") from exc

    plain_text = _extract_plain_text(result)
    chunks = list(getattr(result, "chunks", []) or [])
    segments_raw = _build_segments(chunks, duration_ms, plain_text, req.log_segments)
    if not plain_text:
        plain_text = " ".join(s["text"] for s in segments_raw).strip()
    detected_language = _extract_detected_language(result, req.language_mode, req.language_code)

    return TranscribeResponse(
        plain_text=plain_text,
        segments=[TranscriptSegment(**s) for s in segments_raw],
        detected_language=detected_language,
        duration_ms=duration_ms,
    )


@app.post("/v1/audio/transcriptions")
async def transcribe_openai(
    file: UploadFile = File(...),
    model: str = Form(...),
    language: str | None = Form(default=None),
    response_format: str = Form(default="json"),
    device: str = Form(default="GPU"),
) -> StreamingResponse:
    """OpenAI-compatible transcription endpoint."""
    if model not in _CATALOG:
        raise HTTPException(status_code=404, detail=f"Model '{model}' is not in the catalog.")
    if not _is_installed(model):
        raise HTTPException(
            status_code=503,
            detail=f"Model '{model}' is not installed. Use POST /models/download to install it first.",
        )

    try:
        resolved_device = _resolve_device(device)
    except RuntimeError as exc:
        raise HTTPException(status_code=503, detail=str(exc)) from exc

    model_path = str(_model_dir(model))
    try:
        pipeline = _get_or_load_pipeline(model_path, resolved_device)
    except Exception as exc:
        raise HTTPException(
            status_code=503,
            detail=_format_model_load_error(exc, model_path, device, resolved_device),
        ) from exc

    audio_bytes = await file.read()
    try:
        samples, duration_ms = _load_wave_bytes(audio_bytes)
    except (ValueError, Exception) as exc:
        raise HTTPException(status_code=400, detail=f"Failed to read audio: {exc}") from exc

    language_mode = "Fixed" if language else "Auto"
    config = _build_generation_config(pipeline, language_mode, language)

    try:
        result = pipeline.generate(samples, generation_config=config)
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"Transcription failed: {exc}") from exc

    plain_text = _extract_plain_text(result)
    chunks = list(getattr(result, "chunks", []) or [])
    segments_raw = _build_segments(chunks, duration_ms, plain_text, _log_segments_default)
    if not plain_text:
        plain_text = " ".join(s["text"] for s in segments_raw).strip()
    detected_language = _extract_detected_language(result, language_mode, language)

    if response_format == "verbose_json":
        oa_segments = [
            OpenAiSegment(
                id=i,
                start=s["start_ms"] / 1000.0,
                end=s["end_ms"] / 1000.0,
                text=s["text"],
            )
            for i, s in enumerate(segments_raw)
        ]
        body = OpenAiTranscriptionResponse(
            language=detected_language,
            duration=duration_ms / 1000.0,
            text=plain_text,
            segments=oa_segments,
        )
        return StreamingResponse(iter([body.model_dump_json()]), media_type="application/json")
    else:
        return StreamingResponse(
            iter([json.dumps({"text": plain_text})]),
            media_type="application/json",
        )


# ---------------------------------------------------------------------------
# Startup
# ---------------------------------------------------------------------------

def _configure(models_path: str, model_download_base_url: str, log_segments: bool) -> None:
    global _models_path, _model_download_base_url, _log_segments_default
    _models_path = models_path
    _model_download_base_url = model_download_base_url.rstrip("/")
    _log_segments_default = log_segments
    Path(_models_path).mkdir(parents=True, exist_ok=True)
    log(f"OpenVINO sidecar: models_path={_models_path} model_download_base_url={_model_download_base_url}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="OpenVINO Whisper FastAPI sidecar")
    parser.add_argument("--port", type=int, default=15432, help="Port to listen on")
    parser.add_argument("--host", default="127.0.0.1", help="Host to bind to")
    parser.add_argument(
        "--models-path", default="./data/models/openvino-genai",
        help="Directory for storing downloaded models",
    )
    parser.add_argument(
        "--model-download-base-url",
        default=os.environ.get("MODEL_DOWNLOAD_BASE_URL", "https://huggingface.co"),
        help="Base URL for HuggingFace model downloads (env: MODEL_DOWNLOAD_BASE_URL)",
    )
    parser.add_argument(
        "--log-segments", action="store_true", default=False,
        help="Log each transcript segment to stderr",
    )
    args = parser.parse_args()

    _configure(args.models_path, args.model_download_base_url, args.log_segments)
    log(f"OpenVINO Whisper sidecar starting on {args.host}:{args.port}")
    uvicorn.run(app, host=args.host, port=args.port, log_level="warning")
