# Shared API Contract and DTO Specification
## TranscriptLab Nova MVP
### Shared contract for React frontend and .NET backend agents

## 1. Purpose

This document defines the shared API contract for the TranscriptLab Nova MVP.

Its purpose is to keep the frontend and backend implementations aligned on:

- Routes
- Request/response DTOs
- Domain enums
- Validation expectations
- Status transitions
- Upload behavior
- Transcript and export behavior

This document is intentionally practical and MVP-focused.

---

## 2. General API conventions

## Base path
All API routes are under:

```text
/api
```

## Content types
Use:

- `application/json` for standard request/response bodies
- `multipart/form-data` for file upload
- media content types for playback/download responses

## Date and time
All date/time values must be UTC ISO 8601 strings.

Examples:
- `2026-04-01T18:25:43Z`
- `2026-04-01T18:25:43.512Z`

## IDs
Use GUID strings for resource identifiers.

Example:
- `c0c8f7e8-8730-4ff4-9215-39a71c7df1ae`

## Error response shape
For predictable client handling, backend should return a common error shape:

```json
{
  "code": "validation_error",
  "message": "Folder name is required.",
  "details": {
    "name": ["The name field is required."]
  }
}
```

Recommended fields:
- `code`
- `message`
- `details` optional

## Pagination
Pagination is not required in MVP.

Simple list endpoints may return full result arrays.

---

## 3. Core enums

## ProjectStatus
```text
Draft
Queued
PreparingMedia
Transcribing
Completed
Failed
Cancelled
```

For MVP, `Uploading` is not a durable project status.
Upload progress should be treated as an HTTP request concern rather than persisted project lifecycle state.

## MediaType
```text
Audio
Video
Unknown
```

## LanguageMode
```text
Auto
Fixed
```

## TranscriptViewMode
```text
Readable
Timestamped
```

## TranscriptionEngine
```text
Whisper
SherpaOnnx
```

Implementation note:
- `SherpaOnnx` is selectable, but backend deployments must provide the local Sherpa runtime/assets required by that engine.

## ModelName
Suggested allowed values for MVP:
```text
tiny
base
small
```

The backend may allow extension, but frontend should default to the above MVP-safe values.
Current implementation note:
- `Whisper` supports `tiny`, `base`, `small`, `medium`, `large`
- `SherpaOnnx` currently supports `small`, `medium`

---

## 4. Shared DTOs

## 4.1 Folder DTOs

### FolderSummaryDto
```json
{
  "id": "guid",
  "name": "Biology",
  "iconKey": "Science",
  "colorHex": "#2E7D32",
  "projectCount": 12,
  "totalSizeBytes": 1876543210,
  "createdAtUtc": "2026-04-01T18:25:43Z",
  "updatedAtUtc": "2026-04-01T18:25:43Z"
}
```

Fields:
- `id: string`
- `name: string`
- `iconKey: string`
- `colorHex: string`
- `projectCount: number`
- `totalSizeBytes: number | null`
- `createdAtUtc: string`
- `updatedAtUtc: string`

### FolderDetailDto
```json
{
  "id": "guid",
  "name": "Biology",
  "iconKey": "Science",
  "colorHex": "#2E7D32",
  "projectCount": 12,
  "totalSizeBytes": 1876543210,
  "createdAtUtc": "2026-04-01T18:25:43Z",
  "updatedAtUtc": "2026-04-01T18:25:43Z"
}
```

For MVP this can match summary shape.

### CreateFolderRequest
```json
{
  "name": "Biology",
  "iconKey": "Science",
  "colorHex": "#2E7D32"
}
```

### UpdateFolderRequest
```json
{
  "name": "Advanced Biology",
  "iconKey": "MenuBook",
  "colorHex": "#1565C0"
}
```

Validation:
- name required
- name trimmed
- name length reasonable, for example 1-120 chars
- `iconKey` optional on create/update; when provided it must be a valid MUI icon component name such as `Folder`, `Science`, or `TravelExploreOutlined`
- `colorHex` optional on create/update; when provided it must be a `#RRGGBB` hex color
- when `iconKey` or `colorHex` are omitted on create/update, the backend should apply safe defaults

---

## 4.2 Project settings DTOs

### ProjectSettingsDto
```json
{
  "engine": "Whisper",
  "model": "small",
  "languageMode": "Auto",
  "languageCode": null,
  "audioNormalizationEnabled": true,
  "diarizationEnabled": false
}
```

Fields:
- `engine: string`
- `model: string`
- `languageMode: string`
- `languageCode: string | null`
- `audioNormalizationEnabled: boolean`
- `diarizationEnabled: boolean`

Rules:
- if `languageMode` is `Auto`, `languageCode` may be null
- if `languageMode` is `Fixed`, `languageCode` should be provided

---

## 4.3 Transcript DTOs

### TranscriptSegmentDto
```json
{
  "startMs": 1250,
  "endMs": 4920,
  "text": "Today we are going to talk about mitochondria.",
  "speaker": null
}
```

Fields:
- `startMs: number`
- `endMs: number`
- `text: string`
- `speaker: string | null`

### TranscriptDto
```json
{
  "projectId": "guid",
  "plainText": "Today we are going to talk about mitochondria.",
  "detectedLanguage": "es",
  "durationMs": 3600000,
  "segmentCount": 1240,
  "segments": [
    {
      "startMs": 0,
      "endMs": 4120,
      "text": "Hoy vamos a hablar de las mitocondrias.",
      "speaker": null
    }
  ],
  "createdAtUtc": "2026-04-01T18:25:43Z",
  "updatedAtUtc": "2026-04-01T18:25:43Z"
}
```

Fields:
- `projectId: string`
- `plainText: string`
- `detectedLanguage: string | null`
- `durationMs: number | null`
- `segmentCount: number`
- `segments: TranscriptSegmentDto[]`
- `createdAtUtc: string`
- `updatedAtUtc: string`

---

## 4.4 Project DTOs

### ProjectSummaryDto
```json
{
  "id": "guid",
  "folderId": "guid",
  "folderName": "Biology",
  "name": "Clase 01 - Células",
  "originalFileName": "Clase 01 - Células.mp4",
  "status": "Queued",
  "progress": 0,
  "mediaType": "Video",
  "durationMs": null,
  "totalSizeBytes": 268435456,
  "createdAtUtc": "2026-04-01T18:25:43Z",
  "updatedAtUtc": "2026-04-01T18:25:43Z"
}
```

Fields:
- `id: string`
- `folderId: string`
- `folderName: string`
- `name: string`
- `originalFileName: string`
- `status: ProjectStatus`
- `progress: number | null`
- `mediaType: MediaType`
- `durationMs: number | null`
- `totalSizeBytes: number | null`
- `createdAtUtc: string`
- `updatedAtUtc: string`

### ProjectDetailDto
```json
{
  "id": "guid",
  "folderId": "guid",
  "folderName": "Biology",
  "name": "Clase 01 - Células",
  "originalFileName": "Clase 01 - Células.mp4",
  "status": "Completed",
  "progress": 100,
  "mediaType": "Video",
  "durationMs": 3600000,
  "totalSizeBytes": 412345678,
  "createdAtUtc": "2026-04-01T18:25:43Z",
  "updatedAtUtc": "2026-04-01T18:25:43Z",
  "queuedAtUtc": "2026-04-01T18:26:00Z",
  "startedAtUtc": "2026-04-01T18:26:05Z",
  "completedAtUtc": "2026-04-01T18:34:55Z",
  "failedAtUtc": null,
  "errorMessage": null,
  "settings": {
    "engine": "Whisper",
    "model": "small",
    "languageMode": "Auto",
    "languageCode": null,
    "audioNormalizationEnabled": true,
    "diarizationEnabled": false
  },
  "mediaUrl": "/api/projects/guid/media",
  "transcriptAvailable": true,
  "availableExports": ["txt", "md", "html", "pdf"],
  "originalFileSizeBytes": 385000000,
  "workspaceSizeBytes": 27345678
}
```

Fields:
- all summary fields
- `queuedAtUtc: string | null`
- `startedAtUtc: string | null`
- `completedAtUtc: string | null`
- `failedAtUtc: string | null`
- `errorMessage: string | null`
- `settings: ProjectSettingsDto`
- `mediaUrl: string`
- `transcriptAvailable: boolean`
- `availableExports: string[]`
- `originalFileSizeBytes: number | null`
- `workspaceSizeBytes: number | null`

---

## 4.5 Queue DTOs

### QueueItemDto
```json
{
  "id": "guid",
  "folderId": "guid",
  "folderName": "Biology",
  "name": "Clase 01 - Células",
  "originalFileName": "Clase 01 - Células.mp4",
  "status": "Queued",
  "progress": 0,
  "mediaType": "Video",
  "durationMs": null,
  "totalSizeBytes": 268435456,
  "engine": "Whisper",
  "model": "small",
  "createdAtUtc": "2026-04-01T18:25:43Z",
  "updatedAtUtc": "2026-04-01T18:25:43Z"
}
```

Fields:
- all `ProjectSummaryDto` fields
- `engine: string`
- `model: string`

### QueueOverviewDto
```json
{
  "queued": [
    {
      "id": "guid",
      "folderId": "guid",
      "folderName": "Biology",
      "name": "Clase 01 - Células",
      "originalFileName": "Clase 01 - Células.mp4",
      "status": "Queued",
      "progress": 0,
      "mediaType": "Video",
      "durationMs": null,
      "totalSizeBytes": 268435456,
      "engine": "Whisper",
      "model": "small",
      "createdAtUtc": "2026-04-01T18:25:43Z",
      "updatedAtUtc": "2026-04-01T18:25:43Z"
    }
  ],
  "processing": [],
  "completed": [],
  "failed": []
}
```

Fields:
- `queued: QueueItemDto[]`
- `processing: QueueItemDto[]`
- `completed: QueueItemDto[]`
- `failed: QueueItemDto[]`

For MVP, recent completed/failed items are enough.

---

## 4.6 Settings DTOs

### GlobalSettingsDto
```json
{
  "defaultEngine": "Whisper",
  "defaultModel": "small",
  "defaultLanguageMode": "Auto",
  "defaultLanguageCode": null,
  "defaultAudioNormalizationEnabled": true,
  "defaultDiarizationEnabled": false,
  "defaultTranscriptViewMode": "Readable"
}
```

Fields:
- `defaultEngine: string`
- `defaultModel: string`
- `defaultLanguageMode: string`
- `defaultLanguageCode: string | null`
- `defaultAudioNormalizationEnabled: boolean`
- `defaultDiarizationEnabled: boolean`
- `defaultTranscriptViewMode: string`

### UpdateGlobalSettingsRequest
Same shape as `GlobalSettingsDto`.

### TranscriptionEngineOptionDto
```json
{
  "engine": "Whisper",
  "models": ["tiny", "base", "small", "medium", "large"]
}
```

Fields:
- `engine: string`
- `models: string[]`

### TranscriptionOptionsDto
```json
{
  "engines": [
    {
      "engine": "Whisper",
      "models": ["tiny", "base", "small", "medium", "large"]
    },
    {
      "engine": "SherpaOnnx",
      "models": ["small", "medium"]
    }
  ]
}
```

Fields:
- `engines: TranscriptionEngineOptionDto[]`

---

## 5. Upload contract

## 5.1 Upload approach

Uploads should use:

```text
POST /api/uploads/batch
Content-Type: multipart/form-data
```

This endpoint creates one project per uploaded file.

## 5.2 Multipart fields

Recommended form-data fields:

### Scalar fields
- `folderId`
- `autoQueue`

### Shared batch settings
- `settings`

`settings` should be a JSON string matching `ProjectSettingsDto`.

### File list
- `files`

Repeat the `files` field once per uploaded file.

### Per-file metadata
- `items`

`items` should be a JSON string array aligned positionally with the `files` list.

Each `items` entry may include:
- `originalFileName`
- `projectName`

## 5.3 Example upload request structure

Conceptual example:

```text
folderId = c0c8f7e8-8730-4ff4-9215-39a71c7df1ae
autoQueue = true
settings = {"engine":"Whisper","model":"small","languageMode":"Auto","languageCode":null,"audioNormalizationEnabled":true,"diarizationEnabled":false}
files = [file1, file2]
items = [{"originalFileName":"class01.mp4","projectName":"Biology Class 01"},{"originalFileName":"class02.mp4","projectName":"Biology Class 02"}]
```

## 5.4 Upload response DTO

### BatchUploadResultDto
```json
{
  "folderId": "guid",
  "createdProjects": [
    {
      "id": "guid",
      "folderId": "guid",
      "folderName": "Biology",
      "name": "Biology Class 01",
      "originalFileName": "class01.mp4",
      "status": "Queued",
      "progress": 0,
      "mediaType": "Video",
      "durationMs": null,
      "createdAtUtc": "2026-04-01T18:25:43Z",
      "updatedAtUtc": "2026-04-01T18:25:43Z"
    }
  ]
}
```

Fields:
- `folderId: string`
- `createdProjects: ProjectSummaryDto[]`

---

## 6. Route specification

## 6.1 Folder routes

### GET `/api/folders`
Returns:
```json
[
  {
    "id": "guid",
    "name": "Biology",
    "projectCount": 12,
    "createdAtUtc": "2026-04-01T18:25:43Z",
    "updatedAtUtc": "2026-04-01T18:25:43Z"
  }
]
```

### GET `/api/folders/{id}`
Returns:
```json
{
  "id": "guid",
  "name": "Biology",
  "projectCount": 12,
  "totalSizeBytes": 1876543210,
  "createdAtUtc": "2026-04-01T18:25:43Z",
  "updatedAtUtc": "2026-04-01T18:25:43Z"
}
```

### POST `/api/folders`
Request:
```json
{
  "name": "Biology"
}
```

Response:
```json
{
  "id": "guid",
    "name": "Biology",
    "projectCount": 0,
    "totalSizeBytes": 0,
    "createdAtUtc": "2026-04-01T18:25:43Z",
    "updatedAtUtc": "2026-04-01T18:25:43Z"
}
```

### PUT `/api/folders/{id}`
Request:
```json
{
  "name": "Advanced Biology"
}
```

Response:
`FolderSummaryDto`

### DELETE `/api/folders/{id}`
Recommended responses:
- `204 No Content` on success
- `409 Conflict` if folder is not empty and deletion is disallowed
- `404 Not Found` if missing

---

## 6.2 Project routes

### GET `/api/projects`
Supported query parameters:
- `folderId`
- `status`
- `search`
- `sort`

Examples:
```text
/api/projects?folderId=guid
/api/projects?status=Completed
/api/projects?folderId=guid&status=Completed
```

Response:
```json
[
  {
    "id": "guid",
    "folderId": "guid",
    "folderName": "Biology",
    "name": "Clase 01 - Células",
    "originalFileName": "Clase 01 - Células.mp4",
    "status": "Completed",
    "progress": 100,
    "mediaType": "Video",
    "durationMs": 3600000,
    "totalSizeBytes": 412345678,
    "createdAtUtc": "2026-04-01T18:25:43Z",
    "updatedAtUtc": "2026-04-01T18:34:55Z"
  }
]
```

### GET `/api/projects/{id}`
Response:
`ProjectDetailDto`

### DELETE `/api/projects/{id}`
Required in MVP.

Recommended responses:
- `204 No Content`
- `404 Not Found`

### POST `/api/projects/{id}/retry`
Response:
`ProjectDetailDto`

Behavior:
- valid only for failed projects in MVP

### POST `/api/projects/{id}/queue`
Response:
`ProjectDetailDto`

Behavior:
- valid only for Draft projects
- transitions the project from Draft to Queued
- returns `409 Conflict` if the project is not in Draft state

### POST `/api/projects/{id}/cancel`
Required in MVP.

Behavior:
- valid for queued items
- may also support active items when the processing stage allows cancellation
- returns updated `ProjectDetailDto`

---

## 6.3 Upload route

### POST `/api/uploads/batch`
Multipart form-data.

Response:
`BatchUploadResultDto`

Errors:
- `400 Bad Request` for invalid request
- `404 Not Found` for missing folder
- `413 Payload Too Large` if configured
- `415 Unsupported Media Type` if strictly enforced

---

## 6.4 Queue route

### GET `/api/queue`
Response:
`QueueOverviewDto`

This route is intended for queue/job dashboard views.

---

## 6.5 Transcript route

### GET `/api/projects/{id}/transcript`
Response:
`TranscriptDto`

If transcript is not ready:
- `409 Conflict` or `404 Not Found` are both possible
- preferred MVP behavior: `409 Conflict` with clear message such as `"Transcript is not available yet."`

---

## 6.6 Media route

### GET `/api/projects/{id}/media`
Response:
- media stream/file
- correct content type
- range request support if practical

Used by browser audio/video player.

---

## 6.7 Export route

### GET `/api/projects/{id}/export?format=txt`
### GET `/api/projects/{id}/export?format=md`
### GET `/api/projects/{id}/export?format=html`
### GET `/api/projects/{id}/export?format=pdf`

Supported query parameters:
- `format`
- `viewMode=readable|timestamped`
- `includeTimestamps=true|false`

Response:
- downloadable file with correct content type
- suggested file name based on project name

Examples:
- `Clase 01 - Células.txt`
- `Clase 01 - Células.md`
- `Clase 01 - Células.html`
- `Clase 01 - Células.pdf`

If transcript is unavailable:
- `409 Conflict`

If format unsupported:
- `400 Bad Request`

---

## 6.8 Settings routes

### GET `/api/settings`
Response:
`GlobalSettingsDto`

### PUT `/api/settings`
Request:
```json

### GET `/api/settings/options`
Response:
`TranscriptionOptionsDto`

Behavior:
- returns the currently supported transcription engines and allowed model names per engine
- frontend should use this route instead of hard-coding engine/model lists
{
  "defaultEngine": "Whisper",
  "defaultModel": "small",
  "defaultLanguageMode": "Auto",
  "defaultLanguageCode": null,
  "defaultAudioNormalizationEnabled": true,
  "defaultDiarizationEnabled": false,
  "defaultTranscriptViewMode": "Readable"
}
```

Response:
`GlobalSettingsDto`

---

## 7. Validation rules

## Folder validation
- name required
- name not whitespace only
- name max length reasonable, for example 120
- `iconKey` optional, but when present it must be a valid MUI icon component name
- `colorHex` optional, but when present it must use `#RRGGBB` format

## Upload validation
- folderId required
- at least one file required
- file size limits configurable
- extension/content type checked reasonably
- project name overrides optional
- project name overrides, when present, must be non-empty after trimming

## Settings validation
- engine must be supported
- model must be supported
- languageMode must be valid
- if languageMode is `Fixed`, languageCode required
- transcript view mode must be valid

## Project retry validation
- project must exist
- retry allowed only in supported states

---

## 8. Status transition rules

Recommended state flow:

```text
Draft -> Queued -> PreparingMedia -> Transcribing -> Completed
```

Failure path:
```text
Queued/PreparingMedia/Transcribing -> Failed
```

Cancellation path:
```text
Queued -> Cancelled
```

Optional:
```text
Transcribing -> Cancelled
```

Retry path:
```text
Failed -> Queued
```

Frontend should treat status as authoritative from backend.

---

## 9. Frontend behavior expectations from API

Frontend should assume:

- a project appears immediately after upload
- project status may change over time via polling/refetch
- transcript is unavailable until project is completed
- media playback URL can be used directly by HTML audio/video elements
- exports may be requested only after transcript completion
- storage usage fields may be null when unavailable or still being calculated

Frontend should not infer completion from progress alone.
Use `status`.

---

## 10. Export format behavior

## Common requirements
All export outputs should include:
- project title
- original filename
- folder name if available
- processing/generated date if available
- transcript body

## Default formatting assumptions
For MVP:
- readable transcript formatting is acceptable
- timestamp inclusion should follow the current UI selection passed by the client
- exact styling does not need to be user-configurable yet

---

## 11. Recommended HTTP status usage

### 200 OK
For successful reads and updates.

### 201 Created
For successful creation, such as folder creation.

### 204 No Content
For successful deletes.

### 400 Bad Request
For malformed request or unsupported values.

### 404 Not Found
For missing folder/project/settings resource.

### 409 Conflict
For state-based conflicts, such as:
- transcript not available yet
- folder cannot be deleted because it contains projects
- retry not allowed in current state

### 413 Payload Too Large
For large uploads beyond configured limits.

### 500 Internal Server Error
For unexpected server errors.

---

## 12. Example frontend TypeScript models

```ts
export type ProjectStatus =
  | "Draft"
  | "Queued"
  | "PreparingMedia"
  | "Transcribing"
  | "Completed"
  | "Failed"
  | "Cancelled";

export type MediaType = "Audio" | "Video" | "Unknown";

export type LanguageMode = "Auto" | "Fixed";

export type TranscriptViewMode = "Readable" | "Timestamped";

export interface FolderSummaryDto {
  id: string;
  name: string;
  iconKey: string;
  colorHex: string;
  projectCount: number;
  totalSizeBytes: number | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface ProjectSettingsDto {
  engine: string;
  model: string;
  languageMode: LanguageMode;
  languageCode: string | null;
  audioNormalizationEnabled: boolean;
  diarizationEnabled: boolean;
}

export interface TranscriptSegmentDto {
  startMs: number;
  endMs: number;
  text: string;
  speaker: string | null;
}

export interface TranscriptDto {
  projectId: string;
  plainText: string;
  detectedLanguage: string | null;
  durationMs: number | null;
  segmentCount: number;
  segments: TranscriptSegmentDto[];
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface QueueItemDto extends ProjectSummaryDto {
  engine: string;
  model: string;
}

export interface ProjectSummaryDto {
  id: string;
  folderId: string;
  folderName: string;
  name: string;
  originalFileName: string;
  status: ProjectStatus;
  progress: number | null;
  mediaType: MediaType;
  durationMs: number | null;
  totalSizeBytes: number | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface ProjectDetailDto extends ProjectSummaryDto {
  queuedAtUtc: string | null;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
  failedAtUtc: string | null;
  errorMessage: string | null;
  settings: ProjectSettingsDto;
  mediaUrl: string;
  transcriptAvailable: boolean;
  availableExports: string[];
  originalFileSizeBytes: number | null;
  workspaceSizeBytes: number | null;
}

export interface QueueOverviewDto {
  queued: QueueItemDto[];
  processing: QueueItemDto[];
  completed: QueueItemDto[];
  failed: QueueItemDto[];
}

export interface GlobalSettingsDto {
  defaultEngine: string;
  defaultModel: string;
  defaultLanguageMode: LanguageMode;
  defaultLanguageCode: string | null;
  defaultAudioNormalizationEnabled: boolean;
  defaultDiarizationEnabled: boolean;
  defaultTranscriptViewMode: TranscriptViewMode;
}
```

---

## 13. Recommended backend C# contract notes

The backend may implement equivalent DTOs as:
- record types
- classes
- response models in Contracts namespace

Important:
- do not expose EF Core entities directly
- keep API contracts stable and explicit

---

## 14. Open issues intentionally left flexible

The following are left implementation-flexible for MVP:
- whether exports are generated on-demand or pre-generated
- whether queue is DB-polled or in-memory coordinated with DB state
- exact upload size limits
- exact search/sort capabilities for `/api/projects`
- whether cancellation is implemented in MVP

---

## 15. Acceptance criteria for shared contract alignment

Frontend and backend are considered aligned when:

1. Folder CRUD uses the DTOs in this document
2. Upload flow creates one project per file
3. Project status and lifecycle match the enum/state rules here
4. Transcript retrieval returns both plain text and segments
5. Media playback uses `/api/projects/{id}/media`
6. Exports use `/api/projects/{id}/export?format=...`
7. Settings APIs use `GlobalSettingsDto`
8. Error and validation handling are predictable enough for the frontend to build against

---

## 16. Implementation note for agents

Both agents should treat this document as the source of truth for:
- route names
- DTO names
- field names
- enum values
- expected API behavior

If either agent introduces changes, this shared contract should be updated first so both sides remain synchronized.
