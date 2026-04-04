# TranscriptLab Nova

TranscriptLab Nova is a self-hosted transcription workspace for recorded classes and lectures. It is designed for homelab deployment and built as an open-source, AI-agent-driven project.

## What It Does

- Organizes recordings into folders and projects
- Uploads audio and video files in batches
- Queues transcription jobs for background processing
- Lets users review transcripts alongside media playback
- Exports transcripts in PDF, Markdown, TXT, and HTML
- Surfaces storage usage for folders and project workspaces

## Project Status

Implementation is underway. Phases 1-3 (scaffold, shared contracts, backend foundation) are complete. The backend and frontend projects build and the database schema is ready.

## Core Documents

- `AGENTS.md`
- `class-transcriber-shared-api-contract.md`
- `class-transcriber-frontend-prd.md`
- `class-transcriber-backend-prd.md`
- `class-transcriber-frontend-tech-stack-requirements.md`
- `class-transcriber-backend-tech-stack-requirements.md`

## Architecture Summary

- Frontend: React, TypeScript, MUI, SWR, wouter
- Backend: ASP.NET Core Minimal APIs, EF Core, SQLite, BackgroundService
- Storage: local filesystem for media, prepared audio, and exports
- Processing: in-process queue with conservative concurrency for homelab hardware

## Target Host

- Intel i3-12100F
- 16 GB RAM
- Intel Arc A310 4 GB

Default processing assumptions:
- one transcription job at a time
- optional GPU acceleration
- conservative model selection

## Working In This Repo

Before implementing anything:

1. Read `AGENTS.md`.
2. Read the relevant PRD.
3. Read `class-transcriber-shared-api-contract.md`.
4. Read the relevant tech stack requirements file.

Do not implement against assumptions that are not documented.

## Planned Implementation Order

1. Repository scaffolding
2. Shared contracts and types
3. Backend storage, folders, and settings
4. Batch upload and project creation
5. Queue worker and transcription pipeline
6. Frontend pages and data flows
7. Exports, playback polish, and Playwright validation

## License

MIT

## Development

### Prerequisites

- .NET 10 SDK
- Node.js 24 LTS
- npm
- FFmpeg (for media processing)

### Backend

```bash
cd src/ClassTranscriber.Api
dotnet run
```

The API runs at `http://localhost:5000`. Swagger UI is available at `/swagger` in development.
In local development, SQLite and all runtime artifacts are stored under the repo-root `data/` directory rather than under `src/ClassTranscriber.Api/`.
When a WhisperNet model is selected for the first time, the backend can download the missing `ggml-*.bin` file into the configured `models/` directory automatically.
The default upload request limit is 1 GiB. Override it with `Uploads__MaxRequestBodySizeBytes` if you need a different ceiling.

### Data Storage

Runtime data is split by environment:

- Local development:
  - SQLite database: `./data/transcriptlab.db`
  - uploads, extracted audio, transcripts, exports, temp files, and downloaded models: `./data/`
- Docker / container runtime:
  - SQLite database: `/data/transcriptlab.db`
  - uploads, extracted audio, transcripts, exports, temp files, and downloaded models: `/data/`

The app intentionally keeps the database and filesystem artifacts under the same configurable base path so a single Docker volume captures the whole workspace.

### Transcription Engines

The backend supports multiple transcription engines. Use `GET /api/settings/options` to query the currently available engines and their models at runtime.

#### SherpaOnnx

Uses the official SherpaOnnx .NET runtime through an isolated helper worker process.

**Model download:**

```bash
# Download all registered models (small + medium)
./scripts/download-sherpa-models.sh all

# Or download a single model
./scripts/download-sherpa-models.sh small
```

Models are placed under the configured path (default `/data/models/sherpa-onnx/<model>/`). When auto-download is enabled, the backend can also fetch a missing registered model on first use.

**Model directory layout:**

Each model directory must contain a `config.json` describing the model backend and file names. Two layouts are supported:

Whisper backend (encoder/decoder pair):
```
/data/models/sherpa-onnx/small/
├── config.json
├── tiny-encoder.onnx
├── tiny-decoder.onnx
└── tiny-tokens.txt
```

SenseVoice backend (single model):
```
/data/models/sherpa-onnx/<model>/
├── config.json
├── model.onnx
└── tokens.txt
```

The `config.json` selects the backend and maps file names. Example for the whisper backend:
```json
{
  "backend": "whisper",
  "encoder": "tiny-encoder.onnx",
  "decoder": "tiny-decoder.onnx",
  "tokens": "tiny-tokens.txt",
  "task": "transcribe"
}
```

Configuration in `appsettings.json`:
```json
{
  "Transcription": {
    "SherpaOnnx": {
      "ModelsPath": "/data/models/sherpa-onnx",
      "Provider": "cpu",
      "NumThreads": 4,
      "AutoDownloadModels": true,
      "LogSegments": false,
      "ModelDownloadBaseUrl": "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models"
    }
  }
}
```

#### WhisperNet (CPU)

Uses the [Whisper.net](https://github.com/sandrohanea/whisper.net) managed library with the `Whisper.net.Runtime` (CPU) native backend through an isolated helper worker process. Models use shared `ggml-*.bin` assets and are auto-downloaded on first use.

No extra setup needed. The NuGet packages are included in the project.

#### WhisperNetCuda (NVIDIA GPU)

Uses the [Whisper.net](https://github.com/sandrohanea/whisper.net) managed library with the stable `Whisper.net.Runtime.Cuda` native backend through the same isolated helper worker process.

**Prerequisites:**

- NVIDIA GPU with CUDA support
- CUDA runtime/driver libraries visible to the app
- For Docker: NVIDIA Container Toolkit and GPU device exposure to the container

Models use shared `ggml-*.bin` assets and are auto-downloaded on first use. The backend probes for CUDA runtime libraries before each job and returns a clear failure if the host/container cannot load them.

#### WhisperNetOpenVino (Intel GPU)

Uses the [Whisper.net](https://github.com/sandrohanea/whisper.net) managed library with the `Whisper.net.Runtime.OpenVino` native backend for Intel GPU acceleration. Ideal for the homelab target (Intel Arc A310).

**Prerequisites:**

- [OpenVino Toolkit (>= 2024.4)](https://github.com/openvinotoolkit/openvino)

Models use shared `ggml-*.bin` assets and are auto-downloaded on first use. The backend probes for the OpenVino runtime before each job and returns a clear failure if the host cannot load the required libraries.

#### OpenVinoGenAi (Intel GPU, separate image)

Uses a separate isolated Python worker backed by `openvino-genai` and pre-exported public Whisper models from the `OpenVINO/*-ov` model catalog. This engine is intentionally separate from `WhisperNetOpenVino`; it targets a newer OpenVINO runtime/toolchain, ships in its own image variant, and should run on an Intel OpenVINO runtime base image rather than a plain ASP.NET runtime image.

Recommended starting point for the target Intel Arc A310 4 GB:

- `base-int8`

Additional curated models:

- `tiny-int8`
- `small-fp16`

Models are downloaded into `/data/models/openvino-genai/<model>/` through the settings model manager or automatically on first use when enabled. The engine uses `GPU` by default and can be changed with `Transcription__OpenVinoGenAi__Device`. When `GPU` is used, the worker resolves it to the first actually available OpenVINO GPU device and fails early with the detected device list if no usable Intel GPU is visible in the container.

Configuration for all WhisperNet engines in `appsettings.json`:
```json
{
  "Transcription": {
    "LogSegments": false,
    "WhisperNet": {
      "AutoDownloadModels": true,
      "LogSegments": false
    }
  }
}
```

When `Transcription:LogSegments` or an engine-specific `...:LogSegments` option is set to `true`, the worker logs each decoded transcript segment to container stderr so it appears in `docker compose logs -f`. The default is `false` to avoid noisy logs on long recordings.

### Frontend

```bash
cd src/frontend
npm install
npm run dev
```

The dev server runs at `http://localhost:5173` and proxies `/api` requests to the backend.

### Build

```bash
# Backend
cd src && dotnet build ClassTranscriber.slnx

# Frontend
cd src/frontend && npm run build

# Tests
cd src && dotnet test ClassTranscriber.slnx
cd src/frontend && npm test
```

### Docker

```bash
docker compose up --build
```

The default image uses `Dockerfile` and is intended for CPU-only runs. The application runs at `http://localhost:5000` with data persisted in a Docker volume.
Large uploads use the same 1 GiB default ceiling in Docker through `appsettings.json`. Override it with `Uploads__MaxRequestBodySizeBytes` in Compose if needed.

To try NVIDIA CUDA inside Docker, use the optional override:

```bash
docker compose -f docker-compose.yml -f docker-compose.cuda.yml up --build
```

The override switches the build to `Dockerfile.cuda`, which uses an NVIDIA CUDA runtime base image and installs `aspnetcore-runtime-10.0` inside it. This requires the NVIDIA Container Toolkit on the host so the container can access the GPU and driver libraries.

To try Intel OpenVINO inside Docker, use the optional override:

```bash
docker compose -f docker-compose.yml -f docker-compose.openvino.yml up --build
```

The OpenVINO override switches the build to `Dockerfile.openvino`, exposes `/dev/dri`, and sets `Transcription__WhisperNet__OpenVinoDevice=GPU` by default. The OpenVINO image is intentionally pinned to the 2024.4 runtime ABI because the current Whisper.net OpenVINO native package expects `libopenvino.so.2440`.

For Intel Arc hosts, `/dev/dri` is the device mapping you want. It exposes both the `card*` and `renderD*` nodes that OpenVINO uses. If the machine has both an Intel iGPU and an Arc dGPU, OpenVINO may enumerate the Arc card as `GPU.1` instead of `GPU`. In that case, start the stack like this:

```bash
OPENVINO_DEVICE=GPU.1 docker compose -f docker-compose.yml -f docker-compose.openvino.yml up --build
```

To try the separate OpenVINO GenAI path, use:

```bash
docker compose -f docker-compose.yml -f docker-compose.openvino-genai.yml up --build
```

This override switches the build to `Dockerfile.openvino-genai`, exposes `/dev/dri`, and sets `Transcription__OpenVinoGenAi__Device=GPU` by default. The image is based on the Intel OpenVINO runtime, which already includes the Intel GPU compute runtime packages. The container layer only adds app dependencies plus `clinfo` for diagnostics. If the Arc card is enumerated as the second OpenVINO GPU device on that host, start it with:

```bash
OPENVINO_GENAI_DEVICE=GPU.1 docker compose -f docker-compose.yml -f docker-compose.openvino-genai.yml up --build
```

To inspect the host DRM nodes:

```bash
ls -l /dev/dri
```

### CasaOS

For CasaOS custom installs, use `docker-compose.casaos.yml`. It is image-based rather than build-based and defaults to the published CPU package:

```bash
ghcr.io/snavatta/transcriptlab-nova-cpu:latest
```

The CasaOS file stores all runtime data under:

```bash
/DATA/AppData/$AppID/data
```

If you want a GPU-backed CasaOS install, change the image in that file to one of:

- `ghcr.io/snavatta/transcriptlab-nova-cuda:latest`
- `ghcr.io/snavatta/transcriptlab-nova-openvino:latest`
- `ghcr.io/snavatta/transcriptlab-nova-openvino-genai:latest`

For OpenVINO on CasaOS, also add:

```yaml
devices:
  - /dev/dri:/dev/dri
environment:
  Transcription__WhisperNet__OpenVinoDevice: GPU
```

If the Arc card is the second OpenVINO GPU device on that host, use `GPU.1` instead.

For OpenVINO GenAI on CasaOS, use:

```yaml
devices:
  - /dev/dri:/dev/dri
environment:
  Transcription__OpenVinoGenAi__Device: GPU
```

For CUDA on CasaOS, the host still needs NVIDIA Container Toolkit and GPU runtime support.

### Public Container Images

The repository is set up to publish four public GHCR images from GitHub Actions:

- `ghcr.io/<owner>/transcriptlab-nova-cpu`
- `ghcr.io/<owner>/transcriptlab-nova-cuda`
- `ghcr.io/<owner>/transcriptlab-nova-openvino`
- `ghcr.io/<owner>/transcriptlab-nova-openvino-genai`

Each package is published for `linux/amd64` and receives:

- `latest`
- the pushed semver tag such as `v1.2.3`
- an immutable `sha-...` tag

Example pulls:

```bash
docker pull ghcr.io/<owner>/transcriptlab-nova-cpu:latest
docker pull ghcr.io/<owner>/transcriptlab-nova-cuda:latest
docker pull ghcr.io/<owner>/transcriptlab-nova-openvino:latest
docker pull ghcr.io/<owner>/transcriptlab-nova-openvino-genai:latest
```

Example runs:

```bash
# CPU
docker run --rm -p 5000:5000 -v transcriptlab-data:/data \
  ghcr.io/<owner>/transcriptlab-nova-cpu:latest

# CUDA
docker run --rm -p 5000:5000 -v transcriptlab-data:/data \
  --gpus all \
  -e NVIDIA_VISIBLE_DEVICES=all \
  -e NVIDIA_DRIVER_CAPABILITIES=compute,utility \
  ghcr.io/<owner>/transcriptlab-nova-cuda:latest

# OpenVINO
docker run --rm -p 5000:5000 -v transcriptlab-data:/data \
  --device /dev/dri:/dev/dri \
  -e Transcription__WhisperNet__OpenVinoDevice=GPU \
  ghcr.io/<owner>/transcriptlab-nova-openvino:latest

# OpenVINO GenAI
docker run --rm -p 5000:5000 -v transcriptlab-data:/data \
  --device /dev/dri:/dev/dri \
  -e Transcription__OpenVinoGenAi__Device=GPU \
  ghcr.io/<owner>/transcriptlab-nova-openvino-genai:latest
```

If your GitHub Packages defaults keep newly published container packages private, set the package visibility to `Public` after the first publish.

Host prerequisites:

- CPU image:
  - no GPU runtime required
- CUDA image:
  - NVIDIA GPU
  - NVIDIA Container Toolkit
  - driver/runtime libraries available to the container
- OpenVINO image:
  - Intel GPU or supported Intel accelerator
  - `/dev/dri` device exposure into the container
  - host graphics stack/driver support compatible with OpenVINO GPU execution
- OpenVINO GenAI image:
  - Intel GPU or supported Intel accelerator
  - `/dev/dri` device exposure into the container
  - host graphics stack/driver support compatible with OpenVINO GPU execution
  - Python OpenVINO GenAI runtime supplied by the dedicated image
