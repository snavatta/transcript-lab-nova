# TASKS.md

This file defines the preferred implementation order for AI agents working in autopilot mode.

## Phase 1: Repository Scaffold

- Create backend project structure under `src/`
- Create frontend project structure under `src/` or repo layout chosen by implementation
- Add formatting, linting, and test scaffolding
- Add Docker and local dev baseline

Definition of done:
- projects build
- baseline CI runs
- repo structure matches docs

## Phase 2: Shared Contracts

- Implement frontend and backend DTO/type definitions from `class-transcriber-shared-api-contract.md`
- Implement shared enums and status handling
- Lock route names and request/response models

Definition of done:
- no route or DTO drift from the contract
- backend OpenAPI shape matches the shared contract
- frontend client types align with backend responses

## Phase 3: Backend Foundation

- Implement configuration, logging, SQLite, EF Core migrations, and file storage
- Implement folder CRUD
- Implement global settings persistence
- Implement storage-accounting model fields

Definition of done:
- folder and settings endpoints work
- data persists across runs
- storage metadata fields exist in backend models and DTOs

## Phase 4: Upload and Project Creation

- Implement `/api/uploads/batch`
- Persist one project per file
- Apply settings snapshots
- Support project deletion

Definition of done:
- batch upload works from contract-defined payload shape
- projects are created correctly
- storage metadata begins populating

## Phase 5: Queue and Processing

- Implement queue overview endpoint
- Implement `BackgroundService` worker
- Implement status transitions
- Implement retry and cancel behavior

Definition of done:
- queued projects process in order
- cancel and retry behave per spec
- queue endpoint exposes required fields

## Phase 6: Media and Transcription

- Implement FFmpeg integration
- Implement media inspection/extraction/normalization
- Implement Whisper engine abstraction and MVP engine path
- Persist transcript text and segments

Definition of done:
- audio and video files can be processed
- transcript data is persisted and retrievable
- failure states are recorded cleanly

## Phase 7: Frontend Foundation

- Implement app shell, routing, and providers
- Implement dashboard, folders, queue, project detail, and settings pages
- Implement API client layer against the shared contract

Definition of done:
- users can navigate all MVP pages
- key loading, empty, and error states exist
- no undocumented routes or DTO assumptions appear in the UI

## Phase 8: Frontend Functional Flows

- Implement folder management
- Implement folder-scoped uploads
- Implement batch review UI
- Implement queue monitoring
- Implement project workspace with transcript modes, search, export, and storage visibility

Definition of done:
- core MVP user flow works end to end
- playback controls follow the documented behavior
- storage usage is visible when available

## Phase 9: Exports and Polish

- Implement TXT, Markdown, HTML, and PDF exports
- Implement current-view-driven export parameters
- Improve notifications and edge-case handling

Definition of done:
- exports match the API contract
- current UI view drives export presentation options
- failure paths remain user-visible and recoverable

## Phase 10: Test and Validate

- Add backend unit/integration tests
- Add frontend unit/component tests
- Add Playwright validation for critical flows

Critical flows:
- create folder
- upload batch into folder
- queue and process project
- open completed project
- playback media
- search transcript
- export transcript
- view storage usage

Definition of done:
- tests cover critical MVP flows
- CI passes
- docs and implementation still align
