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

#### OpenVinoWhisperSidecar (Intel GPU)

Uses the supported OpenVINO Whisper sidecar for Intel GPU acceleration. The backend starts a local FastAPI sidecar process that loads OpenVINO Whisper models through the `openvino-genai` Python package and exposes an OpenAI-compatible `/v1/audio/transcriptions` endpoint.

**Prerequisites:**

- Python 3 with the sidecar requirements from `src/ClassTranscriber.Api/Tools/requirements-openvino-sidecar.txt`
- Intel GPU runtime support visible to the host or container

OpenVINO Whisper models are stored under `data/models/openvino-genai/` and can be downloaded on first use or through `POST /api/settings/models/manage`.

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

The OpenVINO override switches the build to `Dockerfile.openvino`, exposes `/dev/dri`, and sets `Transcription__OpenVinoWhisperSidecar__Device=GPU` by default.

For Intel Arc hosts, the OpenVINO image also needs the system Intel OpenCL libraries to take precedence over older Intel compiler libraries inherited from the base image. `Dockerfile.openvino` now enforces that linker order, which was required to restore GPU compilation inside Docker on the Arc A310 used to develop and validate this project.

For Intel Arc hosts, `/dev/dri` is the device mapping you want. It exposes both the `card*` and `renderD*` nodes that OpenVINO uses. In the validated Docker setup for this repo, the Arc A310 is exposed as `GPU` inside the container. If a host enumerates multiple Intel GPUs differently, inspect the sidecar `/devices` output before overriding the device name.

```bash
OPENVINO_DEVICE=GPU docker compose -f docker-compose.yml -f docker-compose.openvino.yml up --build
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

For OpenVINO on CasaOS, also add:

```yaml
devices:
  - /dev/dri:/dev/dri
environment:
  Transcription__OpenVinoWhisperSidecar__Device: GPU
```

If a host reports multiple Intel GPUs in the sidecar `/devices` output, override the device value accordingly.

For CUDA on CasaOS, the host still needs NVIDIA Container Toolkit and GPU runtime support.

### Public Container Images

The repository is set up to publish three public GHCR images from GitHub Actions:

- `ghcr.io/<owner>/transcriptlab-nova-cpu`
- `ghcr.io/<owner>/transcriptlab-nova-cuda`
- `ghcr.io/<owner>/transcriptlab-nova-openvino`

Each package is published for `linux/amd64` and receives:

- `latest`
- the pushed semver tag such as `v1.2.3`
- an immutable `sha-...` tag

Example pulls:

```bash
docker pull ghcr.io/<owner>/transcriptlab-nova-cpu:latest
docker pull ghcr.io/<owner>/transcriptlab-nova-cuda:latest
docker pull ghcr.io/<owner>/transcriptlab-nova-openvino:latest
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
  -e Transcription__OpenVinoWhisperSidecar__Device=GPU \
  ghcr.io/<owner>/transcriptlab-nova-openvino:latest
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
  - Intel GPU or supported Intel accelerator
  - `/dev/dri` device exposure into the container
  - host graphics stack/driver support compatible with OpenVINO GPU execution
  - Python OpenVINO GenAI runtime supplied by the dedicated image
