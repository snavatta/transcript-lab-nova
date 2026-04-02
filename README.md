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

- Intel i3-12100KF
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
- Node.js 22 LTS
- npm
- FFmpeg (for media processing)

### Backend

```bash
cd src/ClassTranscriber.Api
dotnet run
```

The API runs at `http://localhost:5000`. Swagger UI is available at `/swagger` in development.
When a Whisper model is selected for the first time, the backend can download the missing `ggml-*.bin` file into the configured `models/` directory automatically.

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

The application runs at `http://localhost:5000` with data persisted in a Docker volume.
