# OpenVINO Sidecar & Extended Engine PRD

This document describes the requirements for:

1. Rewriting the `OpenVinoWhisperSidecar` Python FastAPI sidecar with a correct, production-quality OpenAI-compatible API and self-managed model downloads
2. Replacing the C# sidecar engine implementation with one that uses `Microsoft.Extensions.AI.ISpeechToTextClient` as an internal abstraction
3. Adding a `OpenAiCompatible` proxy engine capable of forwarding transcription to any OpenAI-compatible `/v1/audio/transcriptions` endpoint, including the local sidecar
4. Adding an `OnnxWhisper` engine placeholder reserved for a future native .NET ONNX implementation
5. Providing real end-to-end tests for the Python sidecar using the host GPU

---

## 1. Python Sidecar Rewrite

### 1.1 Overview

The sidecar (`src/ClassTranscriber.Api/Tools/openvino_whisper_sidecar.py`) is a long-running FastAPI HTTP server that:

- Is spawned lazily by the .NET API on first transcription request that targets `OpenVinoWhisperSidecar`
- Runs on `127.0.0.1` at a configurable port (default `15432`)
- Keeps `openvino_genai.WhisperPipeline` instances cached in memory between jobs to eliminate per-job model load overhead
- Manages its own model downloads independently of the .NET model download infrastructure
- Is killed by the .NET API on application shutdown

### 1.2 Python implementation requirements

The rewritten sidecar must:

1. Use `pipeline.generate(samples, generation_config=config)` with the `generation_config` keyword argument (not a positional argument). The current implementation has a positional argument bug causing `500 vector::reserve` errors.
2. Read chunk timestamps from `chunk.start_ts` and `chunk.end_ts` attributes. These are the correct attribute names as confirmed by the working `openvino_genai_worker.py`. The current implementation incorrectly reads `chunk.timestamps.begin` and `chunk.timestamps.end`.
3. Extract plain text from `result.texts` (list) first; join segments as a fallback when `result.texts` is empty or None.
4. Mirror the device resolution logic from `openvino_genai_worker.py` exactly — including `AUTO`, `GPU`, and explicit device strings — and log resolved device names for observability.
5. Log all pipeline load events and device resolution outcomes to stderr (forwarded to the .NET logger).
6. The pipeline cache is keyed by `{model_path}::{device}` and is loaded lazily on first use.
7. Apply thread-safe locking around the pipeline cache to prevent concurrent duplicate model loads.

### 1.3 HTTP endpoints

#### `GET /health`

Returns `{"status": "ok"}`. No side effects. Used by `.NET` to poll until the sidecar is ready after spawn.

#### `POST /transcribe` (internal, backward compatible)

Internal endpoint used by the C# `OpenVinoWhisperSidecarTranscriptionEngine`. Must remain backward compatible.

Request body (JSON):
```json
{
  "audio_path": "/absolute/path/to/file.wav",
  "model_path": "/absolute/path/to/model-dir",
  "device": "GPU",
  "language_mode": "Auto",
  "language_code": null,
  "log_segments": false
}
```

Response body (JSON):
```json
{
  "plain_text": "...",
  "segments": [
    { "start_ms": 0, "end_ms": 3200, "text": "...", "speaker": null }
  ],
  "detected_language": "en",
  "duration_ms": 15000
}
```

Errors:
- `400` — audio file unreadable or wrong format (not 16kHz mono WAV)
- `503` — model load failed or requested device not available
- `500` — unexpected transcription error

#### `POST /v1/audio/transcriptions` (OpenAI-compatible)

OpenAI-compatible endpoint. Accepts `multipart/form-data`:

| Field | Type | Required | Notes |
|---|---|---|---|
| `file` | binary | yes | 16kHz mono WAV audio file |
| `model` | string | yes | Model name from the catalog (e.g., `tiny-int8`) |
| `language` | string | no | BCP-47 language code. Omit or `null` for auto-detection |
| `response_format` | string | no | `json` (default) or `verbose_json` |

Response (JSON):
```json
{
  "text": "Full transcription plain text",
  "task": "transcribe",
  "language": "en",
  "duration": 15.0,
  "segments": [
    {
      "id": 0,
      "seek": 0,
      "start": 0.0,
      "end": 3.2,
      "text": "...",
      "tokens": [],
      "temperature": 0.0,
      "avg_logprob": 0.0,
      "compression_ratio": 1.0,
      "no_speech_prob": 0.0
    }
  ]
}
```

When `response_format=json`, return only `{"text": "..."}`.

Errors match the internal `/transcribe` endpoint (`400`, `503`, `500`).

#### `GET /v1/models`

Returns models in OpenAI list format:
```json
{
  "object": "list",
  "data": [
    { "id": "tiny-int8", "object": "model", "created": 0, "owned_by": "local" },
    { "id": "small-fp16", "object": "model", "created": 0, "owned_by": "local" }
  ]
}
```

Returns only models that are currently installed (their directory exists with all required files).

#### `GET /models`

Returns all catalog models with download status:
```json
{
  "models": [
    {
      "name": "tiny-int8",
      "display_name": "Whisper Tiny INT8",
      "is_installed": true,
      "install_path": "/data/models/openvino-genai/tiny-int8",
      "size_bytes": 42000000
    }
  ]
}
```

#### `POST /models/download`

Triggers download of a catalog model. Streams SSE events throughout the download:

Request body (JSON):
```json
{ "model": "tiny-int8" }
```

Response: `text/event-stream` (Server-Sent Events), one JSON object per event line:

```
data: {"status": "starting", "model": "tiny-int8", "progress": 0.0}

data: {"status": "downloading", "model": "tiny-int8", "progress": 0.32, "bytes_downloaded": 13000000, "bytes_total": 40000000}

data: {"status": "complete", "model": "tiny-int8", "progress": 1.0}

data: {"status": "error", "model": "tiny-int8", "error": "Connection refused"}
```

Only one concurrent download per model is allowed. If a download is already in progress for the same model, the new request joins the same SSE stream.

#### `DELETE /models/{model_name}`

Deletes the model directory for the named model. Returns `204 No Content` on success. Returns `404` if the model is not installed. Returns `409` if the model is currently loaded in the pipeline cache (cannot delete a live model).

#### `GET /devices`

Returns OpenVINO available devices:
```json
{
  "devices": [
    { "id": "CPU", "name": "Intel Core i7" },
    { "id": "GPU.0", "name": "Intel Arc A310" }
  ]
}
```

### 1.4 Model catalog

The sidecar manages the same shared OpenVINO Whisper model catalog as the C# layer:

| Name | Repository |
|---|---|
| `tiny-int8` | `OpenVINO/whisper-tiny-int8-ov` |
| `tiny-fp16` | `OpenVINO/whisper-tiny-fp16-ov` |
| `base-int8` | `OpenVINO/whisper-base-int8-ov` |
| `base-fp16` | `OpenVINO/whisper-base-fp16-ov` |
| `small-int8` | `OpenVINO/whisper-small-int8-ov` |
| `small-fp16` | `OpenVINO/whisper-small-fp16-ov` |
| `medium-int8` | `OpenVINO/whisper-medium-int8-ov` |
| `medium-fp16` | `OpenVINO/whisper-medium-fp16-ov` |

Models are downloaded from HuggingFace. The base URL is configurable via a `--model-download-base-url` CLI argument or `MODEL_DOWNLOAD_BASE_URL` environment variable (default: `https://huggingface.co`).

Models are stored in the directory given by `--models-path` CLI argument (default: `./data/models/openvino-genai`).

### 1.5 Startup and CLI

The sidecar is started by the .NET `OpenVinoWhisperSidecarManager` and must accept the following CLI arguments:

```
python3 openvino_whisper_sidecar.py [--port PORT] [--host HOST] [--models-path PATH] [--model-download-base-url URL] [--log-segments]
```

All arguments are optional. Default values must match the .NET `OpenVinoWhisperSidecarOptions` defaults.

### 1.6 Requirements file

A `requirements-openvino-sidecar.txt` file must be created in `src/ClassTranscriber.Api/Tools/` listing the pinned Python dependencies needed to run the sidecar:

- `openvino-genai`
- `fastapi[standard]`
- `uvicorn[standard]`
- `numpy`
- `httpx` (for E2E test client)

---

## 2. C# Sidecar Engine Rewrite

### 2.1 `ISpeechToTextClient` integration

`Microsoft.Extensions.AI.Abstractions` provides an experimental `ISpeechToTextClient` interface:

```csharp
[Experimental(DiagnosticIds.Experiments.AISpeechToText)]
public interface ISpeechToTextClient : IDisposable
{
    Task<SpeechToTextResponse> GetTextAsync(Stream audioSpeechStream, SpeechToTextOptions? options = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(Stream audioSpeechStream, SpeechToTextOptions? options = null, CancellationToken cancellationToken = default);
}
```

The `ISpeechToTextClient` interface must be used as the internal HTTP-calling abstraction for both the `OpenVinoWhisperSidecar` and `OpenAiCompatible` engines. It must **not** replace `IRegisteredTranscriptionEngine` as the public engine contract.

The `[Experimental]` diagnostic must be suppressed at the call site with `#pragma warning disable MEAI001` or by setting `<NoWarn>MEAI001</NoWarn>` in the project file (prefer the latter).

### 2.2 Request handling

When `OpenVinoWhisperSidecarTranscriptionEngine.TranscribeAsync()` is called:

1. Ensure the sidecar process is running via `IOpenVinoWhisperSidecarManager.EnsureStartedAsync()`
2. Ensure the requested model is installed via `IOpenVinoSidecarModelManager.EnsureModelInstalledAsync(model, ct)`
3. Open the prepared audio WAV file as a `Stream`
4. Call `ISpeechToTextClient.GetTextAsync(stream, options, ct)` where `options` carries language mode and language code
5. Map the `SpeechToTextResponse` back to `TranscriptionResult`

### 2.3 `IOpenVinoSidecarModelManager`

```csharp
public interface IOpenVinoSidecarModelManager
{
    Task EnsureModelInstalledAsync(string model, CancellationToken cancellationToken);
    Task<IReadOnlyList<SidecarModelStatusDto>> ListModelsAsync(CancellationToken cancellationToken);
}
```

`EnsureModelInstalledAsync` calls `GET /models` on the sidecar first; if the model is not installed, it calls `POST /models/download` and consumes the SSE stream until `status=complete` or `status=error`. Progress events are logged via `ILogger`. If `status=error`, a `TranscriptionException` is thrown.

`ListModelsAsync` calls `GET /models` on the sidecar and returns the result.

### 2.4 C# model download delegation

The C# `OpenVinoWhisperSidecarTranscriptionEngine` must **not** use the legacy C# OpenVINO model download path. Model download is fully delegated to the sidecar via `IOpenVinoSidecarModelManager`.

The `TranscriptionModelManagerService.CreateRegistration()` for `OpenVinoWhisperSidecar` must use `IOpenVinoSidecarModelManager.EnsureModelInstalledAsync()` for download actions rather than the legacy C# OpenVINO model downloader.

### 2.5 Sidecar process lifecycle

The sidecar process must be killed on `IHostApplicationLifetime.ApplicationStopping` in addition to `IAsyncDisposable.DisposeAsync()`. This ensures the child process is cleaned up even when the application host stops without explicitly disposing the manager.

### 2.6 `OpenVinoSidecarSpeechToTextClient`

Implements `ISpeechToTextClient`. Sends audio as a `multipart/form-data` POST to the sidecar's `/v1/audio/transcriptions` endpoint:

- `file` = WAV stream with filename `audio.wav`
- `model` = model name (e.g., `tiny-int8`)
- `language` = BCP-47 code or omitted for auto
- `response_format` = `verbose_json`

Maps the OpenAI verbose JSON response back to `SpeechToTextResponse.Contents`.

---

## 3. `OpenAiCompatible` Engine

### 3.1 Overview

`OpenAiCompatible` is a generic proxy engine that forwards transcription requests to any external service that exposes an OpenAI-compatible `/v1/audio/transcriptions` endpoint. This includes:

- The local OpenVINO sidecar (same host, different port)
- Whisper.cpp HTTP server
- Ollama
- Any hosted model API

### 3.2 Configuration

`OpenAiCompatibleOptions`:

| Field | Type | Default | Notes |
|---|---|---|---|
| `BaseUrl` | string | `""` | Full base URL of the target API, e.g., `http://localhost:15432` |
| `ApiKey` | string | `""` | Optional bearer token; omitted when empty |
| `ModelName` | string | `""` | Model name sent in the `model` field; required |
| `TimeoutSeconds` | int | `120` | Per-request timeout |

### 3.3 Availability

The engine is hidden from `GET /api/settings/options` and reports unavailable in `GET /api/diagnostics` when `BaseUrl` is not configured or `ModelName` is empty. The availability error message must be clear: `"OpenAiCompatible engine is not configured. Set BaseUrl and ModelName in appsettings.json."`.

### 3.4 Availability probe

When configured, `GetProbeError()` sends `GET {BaseUrl}/v1/models` with a 5-second timeout. If the request fails or returns non-2xx, the probe returns an error string. If it succeeds, the probe returns `null`.

### 3.5 `OpenAiCompatibleSpeechToTextClient`

Implements `ISpeechToTextClient`. Sends audio as multipart POST to `{BaseUrl}/v1/audio/transcriptions` with an optional `Authorization: Bearer {ApiKey}` header when `ApiKey` is non-empty.

Request fields match `OpenVinoSidecarSpeechToTextClient`, with `model` set from `OpenAiCompatibleOptions.ModelName`.

### 3.6 Model listing

`SupportedModels` in `OpenAiCompatibleTranscriptionEngine` attempts `GET {BaseUrl}/v1/models` and returns model IDs. On failure, returns `[OpenAiCompatibleOptions.ModelName]` as a single-item list.

### 3.7 Shared HTTP logic

`OpenVinoSidecarSpeechToTextClient` and `OpenAiCompatibleSpeechToTextClient` must share HTTP multipart construction code via a common private helper or base class rather than duplicating it.

---

## 4. `OnnxWhisper` Engine Placeholder

### 4.1 Overview

`OnnxWhisper` is a future engine that will perform native in-process Whisper transcription using `Microsoft.ML.OnnxRuntime` and pre-exported ONNX model files. It is added as a registered placeholder in this release.

### 4.2 Behavior in this release

`OnnxWhisperTranscriptionEngine.GetAvailabilityError()` must return:

```
"OnnxWhisper engine is not yet implemented. It is reserved for a future release using Microsoft.ML.OnnxRuntime."
```

`TranscribeAsync()` must throw `NotImplementedException` if called directly.

`SupportedModels` returns an empty collection.

The engine is registered and visible in `GET /api/diagnostics` with its `isAvailable = false` and the above error message. It must **not** appear in `GET /api/settings/options` because it has no supported models.

### 4.3 No `Microsoft.ML.OnnxRuntime` NuGet

Do not add `Microsoft.ML.OnnxRuntime` or any ONNX inference package in this release. The placeholder adds only the engine class and enum value.

---

## 5. End-to-End Python Tests

### 5.1 Test location

Tests are placed in `src/ClassTranscriber.Api/Tools/tests/`. They are standalone `pytest` tests and are not part of the .NET test project.

### 5.2 E2E tests (require GPU — Intel Arc A310)

File: `test_sidecar_e2e.py`

These tests start the sidecar as a subprocess, wait for `/health` to respond, then execute real transcription requests against the GPU.

Required test cases:

| Test | What it validates |
|---|---|
| `test_health` | `GET /health` → `200 {"status":"ok"}` |
| `test_devices_includes_gpu` | `GET /devices` → `devices` list contains at least one entry with `id` starting with `GPU` |
| `test_model_download_sse` | `POST /models/download` → SSE stream ends with `status=complete`; model directory exists after |
| `test_transcribe_internal` | `POST /transcribe` with a real WAV file and `tiny-int8` model → `plain_text` non-empty, `segments` non-empty, `duration_ms > 0` |
| `test_transcribe_openai_compatible` | `POST /v1/audio/transcriptions` (multipart, `response_format=verbose_json`) → `text` non-empty, `segments` non-empty |
| `test_pipeline_cache` | Two identical calls to `POST /transcribe` → second call completes in less time than first (proves model stays loaded) |
| `test_transcribe_fixed_language` | `POST /transcribe` with `language_mode=Fixed, language_code=en` → segments non-empty |
| `test_models_list_after_download` | `GET /models` after download → response contains entry for `tiny-int8` with `is_installed=true` |
| `test_model_delete` | `DELETE /models/tiny-int8` → `204`; then `GET /models` → entry has `is_installed=false` |

### 5.3 Unit tests (no GPU required)

File: `test_worker_unit.py`

Tests for pure helper functions in `openvino_genai_worker.py` that do not require a GPU or model:

| Test | What it validates |
|---|---|
| `test_sanitize_text` | `sanitize_text("hello\nworld\r\n")` → `"hello world"` |
| `test_build_segments_fallback` | Empty chunks list + non-empty `plain_text` → returns one segment covering full duration |
| `test_load_wave_expected_shape` | Synthetic 16kHz mono int16 WAV → correct sample count and duration |

### 5.4 Test fixtures

File: `conftest.py`

- `sidecar_process` fixture: starts the sidecar subprocess, waits for `GET /health` to succeed (up to 30 seconds), yields a base URL string, then terminates the process on teardown
- `http_client` fixture: returns an `httpx.Client` pointed at the sidecar
- `test_wav_path` fixture: generates a 3-second 16kHz mono int16 WAV file in a temp directory using only `wave` and `struct`; returns the path

### 5.5 Configuration

File: `pytest.ini` (or `pyproject.toml` `[tool.pytest.ini_options]` section):

- Mark GPU tests with `@pytest.mark.gpu`
- Allow running unit-only tests with `pytest -m "not gpu"`
- Set test timeout to 300 seconds for GPU tests

---

## 6. Frontend Changes Specification

This section specifies the frontend changes required for another agent to implement. The backend changes described in this document are backward-compatible with the existing frontend because `engine` is already typed as `string` in the TypeScript API contract. However, the following display-level changes are needed for a complete user experience.

### 6.1 Engine display labels

Wherever engine names are rendered in the UI (project details, queue items, settings selectors, diagnostics), the following display labels must be added:

| Engine value | Display label |
|---|---|
| `OpenVinoWhisperSidecar` | OpenVINO Sidecar |
| `OnnxWhisper` | ONNX Whisper (coming soon) |
| `OpenAiCompatible` | OpenAI-Compatible API |

### 6.2 Settings page — engine selector

- `OnnxWhisper` must be omitted from the engine selector in the settings page because `GET /api/settings/options` will not include it (it has no supported models). No frontend change is needed; the existing dynamic binding to the options API handles this automatically.
- `OpenAiCompatible` should display a small info tooltip or helper text: "Requires backend configuration in `appsettings.json`. Contact your administrator to configure the target URL and model."
- `OpenVinoWhisperSidecar` should display a note: "Uses a local OpenVINO GPU sidecar. Requires the OpenVINO Python environment to be configured."

### 6.3 Model manager

- The `OpenVinoWhisperSidecar` engine model downloads are now handled by the sidecar process. The C# model manager proxies the download request. From the frontend perspective, model download flow is unchanged — the existing model manager API routes still work.
- Model probe state for `OpenVinoWhisperSidecar` models may take longer on first use because the sidecar must be started. The frontend should not interpret a slow initial probe as an error — the existing loading state in the model manager is sufficient.

### 6.4 `OnnxWhisper` diagnostics display

- Diagnostics page must show `OnnxWhisper` with `isAvailable = false` and the unavailability error message. The existing diagnostics rendering already handles non-available engines; no new component is needed.

### 6.5 `OpenAiCompatible` model selection

- When `OpenAiCompatible` is available (i.e., configured), the model selector will show whatever models the backend returns from `GET /api/settings/options`. The frontend does not need special handling; the existing dynamic selector covers this.

---

## 7. Decision Record

| Decision | Rationale |
|---|---|
| `ISpeechToTextClient` is an internal implementation layer | `IRegisteredTranscriptionEngine` remains the public engine contract; MEAi is an internal calling abstraction |
| Model downloads for `OpenVinoWhisperSidecar` move to the sidecar | Avoids duplicating HuggingFace download logic in C#; sidecar knows its own model directory |
| `OpenAiCompatible` shares HTTP code with sidecar client | Avoids duplication; both call the same `/v1/audio/transcriptions` endpoint shape |
| `OnnxWhisper` is a stub only | Full autoregressive Whisper decoding from raw ONNX in .NET is out of scope; placeholder registers the engine name |
| SSE for model download progress | Streaming gives real-time feedback for large model downloads without blocking the HTTP connection or requiring polling |
| `OpenAiCompatible` hidden when unconfigured | Avoids user confusion when no external API is available |
| Sidecar process killed on `ApplicationStopping` | Ensures child process cleanup even if `DisposeAsync` is not called during shutdown |
