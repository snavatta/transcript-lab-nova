# Product Requirements Document (PRD)
## TranscriptLab Nova Backend MVP
### .NET backend specification for Codex agent consumption

## 1. Overview

Build the backend service for **TranscriptLab Nova**, a self-hosted transcription product focused on **recorded classes and lectures**.

The backend will power a React + MUI frontend and will run in the **same Docker image** as the frontend, exposed on a **different port**.

The backend is responsible for:

- Folder management
- Project creation and lifecycle management
- File upload handling
- Batch queueing of transcription jobs
- Media preprocessing
- Transcription execution through pluggable engines
- Transcript storage and retrieval
- Media playback URL support
- Transcript export in multiple formats
- Global default settings management

This is an **MVP with no authentication/login**.

---

## 2. Product goals

### Primary goal
Provide a reliable local backend for organizing, queueing, transcribing, and exporting recorded classes.

### Secondary goals
- Support batch upload workflows
- Keep domain model simple and practical
- Make transcription engine pluggable
- Support future extension without over-engineering MVP

### Non-goals for MVP
- Multi-user support
- Authentication/authorization
- Public sharing links
- Real-time live transcription
- Distributed workers
- Cloud storage
- Advanced collaboration/editing workflows
- Nested folders
- Full text search across all transcripts
- Transcript manual editing

---

## 3. Technical stack expectations

## Required backend stack
- .NET backend
- ASP.NET Core Web API
- SQLite for persistence in MVP
- Local filesystem storage for media and exports
- Hosted background worker for job processing

## Recommended implementation style
- Single ASP.NET Core application
- REST API
- Minimal APIs
- Background processing using `BackgroundService`
- Simple service layer and repository/data access layer
- Minimal external infrastructure requirements

## Runtime constraints
- Backend and frontend run in the same Docker image
- Backend exposes a different port from frontend
- Backend should be homelab-friendly and easy to deploy
- No mandatory reverse proxy assumptions inside the app

---

## 4. Core domain concepts

### Folder
A top-level grouping for projects.

Examples:
- Biology
- Math
- History

### Project
A single uploaded media file and its transcription job/workspace.

A project includes:
- folder association
- original file metadata
- processing status
- selected transcription settings
- transcript output
- exportable outputs
- storage usage metadata

### Project settings
The effective settings used for the transcription run.

Includes:
- engine
- model
- language selection
- audio normalization enabled/disabled
- diarization enabled/disabled

### Global settings
Default settings applied to future uploads unless overridden.

### Transcript
The textual result of transcription.

The backend should preserve both:
- plain text
- structured transcript segments

### Transcript segment
A timestamped unit of transcript used for:
- timestamped UI display
- playback seeking
- export generation
- future subtitle support

### Storage usage
Storage accounting information used to show disk consumption in the frontend.

Should support:
- per-project original media size
- per-project derived workspace size such as transcripts, exports, and prepared audio
- per-project total workspace size
- per-folder aggregated total size

---

## 5. Functional scope

The backend must support:

- CRUD-style folder operations
- project rename/update of editable metadata such as display name
- Uploading one or more files into a folder
- Automatic project creation per uploaded file
- Queueing projects for processing
- Background transcription processing
- Media extraction/preparation
- Diagnostics reporting for runtime metrics, engine availability, and per-project disk usage
- Retrieval of project/transcript metadata
- Media playback access
- Export generation for PDF, MD, TXT, HTML
- Global defaults management

---

## 6. High-level architecture

## Recommended application structure
Use a single deployable ASP.NET Core app with the following layers:

- API layer
- Application/services layer
- Infrastructure layer
- Background job processor
- Persistence layer
- Local file storage layer

## Processing model
1. User uploads one or more files
2. Backend stores files locally
3. Backend creates one project per file
4. Backend applies current defaults or provided overrides
5. Backend enqueues each project
6. Background worker picks queued projects
7. Worker preprocesses media if needed
8. Worker transcribes using selected engine
9. Worker stores transcript + structured segments
10. Worker generates export representations or on-demand exports
11. Project becomes completed or failed

---

## 7. Recommended project structure

```text
src/
  ClassTranscriber.Api/
    Controllers/ or Endpoints/
    Contracts/
    Services/
    Domain/
    Persistence/
    Jobs/
    Media/
    Transcription/
    Export/
    Settings/
    Storage/
    Program.cs
```

Suggested namespaces/modules:
- `Folders`
- `Projects`
- `Uploads`
- `Queue`
- `Transcription`
- `Media`
- `Exports`
- `Settings`

---

## 8. Domain model requirements

## 8.1 Folder entity
Required fields:
- `Id`
- `Name`
- `IconKey`
- `ColorHex`
- `CreatedAtUtc`
- `UpdatedAtUtc`
- `TotalSizeBytes`

Optional later:
- description

## 8.2 Project entity
Required fields:
- `Id`
- `FolderId`
- `Name`
- `OriginalFileName`
- `StoredFileName`
- `MediaType`
- `FileExtension`
- `MediaPath`
- `Status`
- `Progress`
- `DurationMs`
- `TranscriptionElapsedMs`
- `CreatedAtUtc`
- `UpdatedAtUtc`
- `QueuedAtUtc`
- `StartedAtUtc`
- `CompletedAtUtc`
- `FailedAtUtc`
- `ErrorMessage`
- `OriginalFileSizeBytes`
- `WorkspaceSizeBytes`
- `TotalSizeBytes`

Optional diagnostic fields:
- `TotalProcessingElapsedMs`
- `MediaInspectionElapsedMs`
- `AudioExtractionElapsedMs`
- `AudioNormalizationElapsedMs`
- `ResultPersistenceElapsedMs`

## 8.3 Project settings entity/value object
Required fields:
- `Engine`
- `Model`
- `LanguageMode`
- `LanguageCode`
- `AudioNormalizationEnabled`
- `DiarizationEnabled`

## 8.4 Transcript entity
Required fields:
- `ProjectId`
- `PlainText`
- `StructuredSegmentsJson`
- `CreatedAtUtc`
- `UpdatedAtUtc`

Optional but recommended:
- `DetectedLanguage`
- `DurationMs`
- `SegmentCount`

## 8.5 Transcript segment contract
Required fields:
- `StartMs`
- `EndMs`
- `Text`
- `Speaker` nullable

## 8.6 Global settings entity
Required fields:
- `DefaultEngine`
- `DefaultModel`
- `DefaultLanguageMode`
- `DefaultLanguageCode`
- `DefaultAudioNormalizationEnabled`
- `DefaultDiarizationEnabled`
- `DefaultTranscriptViewMode`

---

## 9. Enumerations and value constraints

## Suggested project status enum
- `Draft`
- `Queued`
- `PreparingMedia`
- `Transcribing`
- `Completed`
- `Failed`
- `Cancelled`

Note for MVP:
- do not persist a separate `Uploading` project status if file upload and project creation happen within a single request
- treat upload progress as an HTTP request concern rather than a durable project lifecycle state
- create the project in `Draft` or `Queued` after the file has been accepted and stored

## Suggested media type enum
- `Audio`
- `Video`
- `Unknown`

## Suggested language mode enum
- `Auto`
- `Fixed`

## Suggested transcript view mode enum
- `Readable`
- `Timestamped`

## Suggested engine enum for MVP
- `WhisperNet`

Current backend extension points may additionally expose:
- `SherpaOnnx`
- `SherpaOnnxSenseVoice`
- `WhisperNet`
- `WhisperNetCuda`
- `OpenVinoWhisperSidecar`
- `OnnxWhisper`
- `OpenAiCompatible`

Implementation note:
- `SherpaOnnx` may run on a local .NET runtime path or isolated helper worker as long as it stays behind the transcription engine abstraction.
- `SherpaOnnxSenseVoice` may run on the same local .NET runtime path or isolated helper worker pattern, but should remain a distinct engine option from Whisper-backed `SherpaOnnx`.
- `WhisperNet` CPU and `WhisperNetCuda` should run through isolated helper worker processes so runtime selection remains per project/job.
- `OpenVinoWhisperSidecar` runs through a long-lived Python FastAPI sidecar with an OpenAI-compatible API. The sidecar manages its own model downloads. The C# engine uses `ISpeechToTextClient` (Microsoft.Extensions.AI experimental) internally. It is the recommended OpenVINO engine for deployments with local GPU hardware.
- `OnnxWhisper` is a reserved placeholder for a future native .NET ONNX Whisper engine. It reports unavailable and must not be used in production.
- `OpenAiCompatible` proxies transcription to any configured OpenAI-compatible API. It must not appear in the engine selector when `BaseUrl` is not configured.

## Suggested model values for MVP
- `tiny`
- `base`
- `small`

The backend should validate allowed values but keep implementation flexible enough to extend.

---

## 10. Storage requirements

## 10.1 Database
Use SQLite in MVP.

The database should store:
- folders
- projects
- settings
- transcript metadata/content
- any queue-relevant state not derivable from project status

## 10.2 Filesystem storage
Use local filesystem storage with configurable base directory.

Recommended folders:
```text
/data/
  uploads/
  audio/
  transcripts/
  exports/
  temp/
  models/
```

### Storage expectations
- uploaded original media goes under `uploads/`
- extracted/normalized audio goes under `audio/`
- generated exports may go under `exports/`
- temporary processing artifacts go under `temp/`
- transcription model files may live under `models/` if needed
- missing ggml WhisperNet model files may be auto-downloaded into `models/` on first use when runtime configuration allows it
- `OpenVinoWhisperSidecar` should keep downloaded pre-exported models under a separate subtree such as `models/openvino-genai/<model>/`
- runtime configuration may optionally enable per-segment worker logging for WhisperNet and SherpaOnnx engines to aid debugging long-running jobs; default behavior should keep this disabled to avoid excessive log volume

### Important storage rule
The backend must not rely on database blobs for large media files.

Store:
- files on disk
- metadata/paths in SQLite

---

## 11. Upload requirements

## 11.1 Supported upload behavior
The backend must support:
- single file upload
- multi-file upload
- upload into a specific folder

## 11.2 Upload result behavior
For each uploaded file:
- store original file
- detect media type if possible
- create a project
- pre-populate project name from filename without extension unless overridden
- attach effective transcription settings
- enqueue project if requested

## 11.3 Batch upload request expectations
The upload API should support:
- one or more files
- target folder id
- optional per-batch settings override
- optional per-file project name override
- optional auto-queue flag

Recommended multipart shape:
- one `files` field repeated for each uploaded file
- one `folderId` field
- one `autoQueue` field
- one `settings` JSON field for shared batch settings
- one `items` JSON field containing per-file metadata in the same order as uploaded files

Each `items` entry should support:
- original client file name
- optional overridden project name

## 11.4 File validation
Validate at minimum:
- folder existence
- allowed size limits
- supported extension/content type as reasonably as possible
- non-empty file

Validation should be practical, not overly strict.

The backend should remain tolerant and rely on processing failure handling when needed.

Upload size limits must be configurable at runtime. The default deployment configuration should accept large single-request class recordings rather than keeping ASP.NET Core's smaller default multipart ceiling.

---

## 12. Media processing requirements

The backend must support uploaded media files that are:
- audio
- video

## 12.1 Media extraction/preparation
If the uploaded file is video:
- extract audio before transcription

If audio normalization is enabled:
- normalize/prepare the audio before transcription

## 12.2 Media processing integration
Use an external media tool such as FFmpeg, wrapped behind an abstraction.

Recommended interfaces:
- `IMediaInspector`
- `IAudioExtractor`
- `IAudioNormalizer`

## 12.3 Stored media metadata
Where possible, retain:
- duration
- detected media type
- content type
- original file name

---

## 13. Transcription engine requirements

## 13.1 Engine abstraction
The backend must not hardcode business logic directly to one engine implementation.

Use an abstraction such as:
- `ITranscriptionEngine`

Required method responsibilities:
- accept prepared media input
- accept transcription settings
- return transcript result including structured segments
- surface errors clearly

## 13.2 MVP engine implementation
Default implementation should target:
- WhisperNet CPU backend
- likely via the Whisper.net helper-worker integration from .NET
- missing local model files should be surfaced clearly and may be fetched on demand into local storage when auto-download is enabled

## 13.3 Transcription result contract
Required output:
- plain text
- segments with start/end timestamps
- detected language if available
- optional speaker labels if diarization exists

## 13.4 Diarization behavior
For MVP:
- backend must accept diarization on/off in settings
- when diarization is enabled, the backend should attempt to assign speaker labels to transcript segments using local processing
- if diarization cannot confidently separate speakers, returning a single consistent speaker label is acceptable
- the code structure should keep diarization as an optional post-processing concern behind the transcription pipeline

Do not over-engineer diarization in MVP.

---

## 14. Background job processing requirements

## 14.1 Processing model
Use a hosted background worker inside the backend application.

Recommended approach:
- `BackgroundService`
- polling for queued projects
- one project at a time initially, or very limited concurrency
- careful status transitions

## 14.2 Job execution flow
For a queued project:
1. mark as `PreparingMedia`
2. inspect media
3. extract/normalize audio if needed
4. mark as `Transcribing`
5. invoke transcription engine
6. store transcript
7. generate exports or export-ready representations
8. mark as `Completed`

On failure:
- mark as `Failed`
- persist concise error message
- preserve enough data for retry

Project creation flow before queueing:
1. accept and store the uploaded file
2. create the project record
3. snapshot effective settings
4. mark the project as `Draft` if not auto-queued, otherwise mark as `Queued`

## 14.3 Concurrency expectations
For MVP:
- start with sequential processing or low configurable concurrency
- do not require distributed queue infrastructure

## 14.4 Retry behavior
Failed projects should be retryable.

Retry should:
- clear prior failure state
- preserve original media
- optionally regenerate transcript and exports
- re-enter queue cleanly

---

## 15. API requirements

The backend should expose a REST API.

Below is the recommended contract shape for MVP.

## 15.1 Folders

### GET `/api/folders`
Returns all folders with summary data.

### POST `/api/folders`
Creates a new folder.

Request body:
- `name`
- optional `iconKey` storing the selected MUI icon component name
- optional `colorHex`

### PUT `/api/folders/{id}`
Renames/updates a folder.

Request body:
- `name`
- optional `iconKey` storing the selected MUI icon component name
- optional `colorHex`

### DELETE `/api/folders/{id}`
Deletes a folder.

MVP behavior:
- allow deletion only when the folder contains no projects
- return a client-friendly validation/conflict response when the folder is not empty

### GET `/api/folders/{id}`
Returns folder detail with summary/project counts.

---

## 15.2 Projects

### GET `/api/projects/{id}`
Returns full project detail.

If recorded, the response may include a diagnostic timing block for frontend debug display and engine/performance comparison.

### GET `/api/projects`
Supports filtering by:
- folder id
- status
- search term
- sort order

Filtering can be simple in MVP.

### DELETE `/api/projects/{id}`
Required in MVP.

Behavior:
- remove DB data
- remove stored files and generated artifacts for the project when safe to do so
- be careful with cleanup of media, prepared audio, transcripts, and exports

### POST `/api/projects/{id}/retry`
Retries a failed project.

Must support:
- retrying with the project's existing settings when no override is provided
- applying an optional settings override before the project is returned to `Queued`
- validating override settings against engines/models currently available in the runtime

### POST `/api/projects/{id}/cancel`
Required in MVP.

Behavior:
- support cancellation for queued projects
- support cancellation for active work when practical for the current processing stage
- return updated project detail

---

## 15.3 Uploads and batch queueing

### POST `/api/uploads/batch`
Multipart/form-data endpoint for one or more files.

Must support:
- `folderId`
- `autoQueue`
- batch settings
- optional per-file project names

Response should include created project summaries.

This is a key MVP endpoint.

### GET `/api/diagnostics`
Returns a diagnostics snapshot for the current backend process.

Must include:
- process CPU usage
- process memory usage
- runtime availability for registered transcription engines
- per-project storage usage values already tracked by the backend

---

## 15.4 Queue and jobs

### GET `/api/queue`
Returns queue/job overview.

Suggested response:
- queued items
- processing items
- completed recent items
- failed items

Each queue item should include at minimum:
- project id
- project name
- folder id
- folder name
- status
- progress
- durationMs if known
- transcriptionElapsedMs if known
- selected engine
- selected model
- createdAtUtc
- totalSizeBytes if known

Alternatively:
- use projects filtered by status and omit a dedicated queue endpoint

Recommendation:
- keep a dedicated queue-friendly endpoint for easier frontend consumption

---

## 15.5 Media playback

### GET `/api/projects/{id}/media`
Streams or serves the original uploaded media.

Requirements:
- support browser playback
- return correct content type if possible
- allow range requests if practical for seeking

Range support is highly desirable for media playback.

### GET `/api/projects/{id}/audio`
Streams or serves the extracted or otherwise prepared transcription audio when that derived file exists.

Requirements:
- return `404` if no derived audio is currently available
- return `audio/wav`
- allow browser playback and seeking when practical

---

## 15.6 Transcript retrieval

### GET `/api/projects/{id}/transcript`
Returns transcript detail.

Recommended response fields:
- project id
- plain text
- segments
- detected language
- transcript summary metadata

---

## 15.7 Export endpoints

### GET `/api/projects/{id}/export?format=txt`
### GET `/api/projects/{id}/export?format=md`
### GET `/api/projects/{id}/export?format=html`
### GET `/api/projects/{id}/export?format=pdf`

Behavior:
- return downloadable file response
- generate on demand or serve cached generated file
- include sensible file name
- accept export presentation options derived from the current UI selection

Recommended query parameters:
- `format`
- `viewMode=readable|timestamped`
- `includeTimestamps=true|false`

---

## 15.8 Settings

### GET `/api/settings`
Returns global defaults.

### PUT `/api/settings`
Updates global defaults.

### GET `/api/settings/models`
Returns the model-management catalog for known engines/models, including filesystem install state, install path, and active probe results for installed models.

### POST `/api/settings/models/manage`
Accepts an engine/model/action payload and returns the updated model-management entry.

Behavior:
- applies only to future uploads
- does not mutate existing projects
- installed models should be actively probed through the real engine/runtime so users can diagnose missing runtimes or broken local model assets without uploading media
- model-management actions must support `Download`, `Redownload`, and `Probe`
- model-management responses must make it clear whether the failure is missing files, runtime unavailability, or probe execution failure

---

## 16. API contract guidelines

## Response conventions
Use consistent response models.

Recommended patterns:
- DTOs/contracts instead of exposing EF entities directly
- validation errors return structured client-friendly messages
- failed job state is reflected in resource state, not only transient errors

## Date handling
Use UTC everywhere in backend contracts.

## IDs
Use GUID/UUID or another stable unique identifier.

Recommendation:
- GUIDs are acceptable and simple for MVP

---

## 17. Export generation requirements

The backend must support export generation for:
- TXT
- MD
- HTML
- PDF

## 17.1 Export source
Exports should be generated from structured transcript data where possible, not only from plain text.

This ensures support for:
- readable vs timestamped rendering
- future subtitle generation
- consistent formatting

## 17.2 Common export contents
Each export should include:
- project title
- original file name
- folder name if available
- processing date if available
- transcript body

## 17.3 Format expectations

### TXT
- plain text
- readable output by default
- timestamps controlled by the current UI selection passed to the export endpoint

### MD
- markdown heading metadata + transcript body

### HTML
- simple readable HTML document
- suitable for browser viewing/printing

### PDF
- printable document version

## 17.4 PDF implementation guidance
Implementation may use:
- HTML-to-PDF generation
- or another practical PDF library

The MVP should prioritize correctness and maintainability over visual complexity.

---

## 18. Transcript representation requirements

Store transcript data in at least two forms:

### Required
- plain text
- structured segments

### Why structured segments matter
They enable:
- timestamp display in UI
- playback seek integration
- multiple export formats
- future subtitle formats like SRT/VTT
- future transcript editing

## Suggested transcript storage approach
- plain text in a text column
- structured segments as JSON in SQLite

---

## 19. Settings behavior requirements

Global settings must:
- be persisted
- be retrievable
- apply to future uploads

Project settings must:
- snapshot the effective settings at queue time
- remain attached to the project even if global defaults later change

This distinction is important and required.

## 19.1 Storage accounting behavior
- storage usage should be persisted or computable without expensive full rescans on every request
- project storage fields should distinguish original media size from derived workspace size
- folder storage totals may be aggregated from projects and updated asynchronously if needed
- if storage usage is temporarily unavailable, the API may return null values instead of blocking core flows

---

## 20. Logging and observability requirements

The backend should produce useful logs for:
- folder creation/update/delete attempts
- uploads
- project creation
- queue transitions
- transcription start/finish/failure
- export generation
- settings updates

Recommended minimum:
- structured logging
- one correlation path per project/job if practical

Do not overcomplicate observability in MVP, but keep logs useful.

---

## 21. Error handling requirements

## Must handle gracefully
- invalid folder id
- invalid upload request
- unsupported or unreadable file
- media preprocessing failure
- transcription engine failure
- export generation failure
- missing project/transcript/export

## Error handling expectations
- return meaningful HTTP status codes
- persist project failure state for processing failures
- keep concise, user-safe error messages in API responses
- log detailed errors internally

---

## 22. Security and access assumptions

MVP assumptions:
- no auth
- homelab/local/private network usage
- no internet-facing hardening required beyond basic safe coding

Even without auth, still do basic safety:
- validate file names/paths
- avoid path traversal
- do not trust client-provided file names
- generate safe stored file names
- constrain storage to configured base paths

## Development/runtime communication
- support frontend-to-backend communication across different local ports in development
- configure CORS explicitly for development origins when needed
- prefer same-origin deployment behavior in the final Dockerized setup where practical

---

## 23. Performance and scalability expectations

## MVP target behavior
- Handles multiple queued files reliably
- Processes one or a few jobs at a time
- Uses local disk efficiently
- Does not require cloud components

## Acceptable MVP tradeoffs
- Queue polling instead of sophisticated broker
- On-demand export generation
- Basic filtering/search instead of advanced indexing

---

## 24. Suggested service abstractions

Recommended service interfaces:

- `IFolderService`
- `IProjectService`
- `IUploadService`
- `IQueueService`
- `ITranscriptionEngine`
- `ITranscriptionService`
- `IMediaInspector`
- `IAudioExtractor`
- `IAudioNormalizer`
- `ITranscriptService`
- `IExportService`
- `ISettingsService`
- `IFileStorage`

This is guidance, not a rigid requirement.

---

## 25. Suggested repository/data abstractions

Optional but useful:
- `IFolderRepository`
- `IProjectRepository`
- `ISettingsRepository`
- `ITranscriptRepository`

Or use EF Core DbContext directly with a modest service layer if preferred.

Do not over-engineer repositories if unnecessary.

---

## 26. Suggested request/response DTOs

## Folder DTO
- `id`
- `name`
- `projectCount`
- `createdAtUtc`
- `updatedAtUtc`
- `totalSizeBytes`

## Project summary DTO
- `id`
- `folderId`
- `folderName`
- `name`
- `originalFileName`
- `status`
- `progress`
- `mediaType`
- `durationMs`
- `totalSizeBytes`
- `createdAtUtc`
- `updatedAtUtc`

## Project detail DTO
Includes summary fields plus:
- settings
- transcript availability
- media URL
- optional derived audio preview URL for video projects when extracted/prepared audio exists
- error message
- completed timestamps
- export availability
- original file size
- workspace size

## Transcript DTO
- `projectId`
- `plainText`
- `segments`
- `detectedLanguage`
- `createdAtUtc`
- `updatedAtUtc`

## Settings DTO
- default engine/model/language/flags
- default transcript view mode

---

## 27. Recommended implementation order

1. Folder CRUD
2. Settings persistence
3. Project persistence
4. Batch upload endpoint
5. File storage service
6. Queue state transitions
7. Background worker
8. Media preprocessing
9. WhisperNet transcription integration
10. Transcript retrieval
11. Media streaming endpoint
12. Export generation
13. Retry behavior
14. Cleanup and polish

---

## 28. Acceptance criteria

The MVP backend is acceptable when it can:

1. Store and return folders
2. Store and return global defaults
3. Accept multiple uploaded files into a folder
4. Create one project per file
5. Persist effective settings per project
6. Queue projects for processing
7. Process queued projects in the background
8. Extract/process media as needed
9. Produce and persist transcript plain text + structured segments
10. Return project detail and transcript detail
11. Serve uploaded media for browser playback
12. Export transcripts as PDF, MD, TXT, and HTML
13. Mark failures clearly and allow retry

---

## 29. Future enhancements (not MVP)

- Multiple transcription engines
- SRT/VTT export
- Full-text transcript search
- Nested folders
- Re-transcription with modified settings
- Bulk export
- Advanced diarization
- OCR for slide extraction from videos
- Summaries/key points generation
- Remote storage backends
- Webhooks/notifications

---

## 30. Codex implementation notes

The backend agent should prioritize:
- pragmatic domain model
- reliable queue state handling
- clean separation of upload, processing, transcript, and export concerns
- simple homelab deployment
- maintainable .NET code

The backend agent should avoid:
- premature microservices
- distributed systems patterns
- auth/user management
- unnecessary abstractions
- premature optimization

The desired outcome is a polished, practical MVP backend for a local transcription application.
