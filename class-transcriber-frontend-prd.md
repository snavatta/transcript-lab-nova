# Product Requirements Document (PRD)
## TranscriptLab Nova Frontend MVP
### React + MUI frontend specification for Codex agent consumption

## 1. Overview

Build the frontend web application for **TranscriptLab Nova**, a self-hosted transcription product focused on **recorded classes and lectures**.

The app allows a user to:

- Organize content into **folders** such as Biology, Math, History, etc.
- Upload **audio and video files**
- Queue **multiple files at once**
- Auto-create a **project** for each uploaded file
- Review transcription settings before queueing
- Monitor job progress
- Open a project workspace to:
  - play the uploaded media
  - read the transcript
  - toggle transcript view modes
  - export the transcript in multiple formats
  - inspect current storage usage for the project workspace

This is an **MVP with no authentication/login**.

The frontend will be consumed by a .NET backend running in the same Docker image, exposed on a different port.

---

## 2. Product goals

### Primary goal
Provide a simple and pleasant interface for uploading recorded classes and reviewing transcripts.

### Secondary goals
- Make batch upload easy
- Make transcript reading and playback comfortable
- Keep the UI structured around folders and projects
- Provide clean defaults while still allowing per-upload overrides

### Non-goals for MVP
- User accounts
- Authentication/authorization
- Sharing/public links
- Collaboration
- Real-time live transcription
- Nested folders
- Rich transcript editing
- Comments/annotations
- Mobile-first optimization beyond responsive usability

---

## 3. Users and use cases

### Primary user
A household user managing recorded class files for study/review.

### Main use cases
1. Create folders such as Biology or Math
2. Upload one or more class recordings into a folder
3. Adjust transcription settings before queueing
4. Wait for processing
5. Open a project and read/play the transcript
6. Export transcript as PDF, Markdown, TXT, or HTML

---

## 4. Core domain concepts

### Folder
A top-level grouping for related projects.

Examples:
- Biology
- Math
- Physics
- English

### Project
A transcription job created from one uploaded file.

Examples:
- `Clase 01 - Células.mp4`
- `Linear Algebra - Matrices.wav`

A project has:
- a name
- an original file
- a status
- processing settings
- transcript data
- exported representations
- storage usage information

### Storage usage
Storage information shown in the UI for folders and projects.

Includes:
- original media size
- transcript/export storage size when available
- total project/workspace size
- aggregated folder storage size

### Transcript settings
Configuration selected when queueing a project.

Includes:
- engine
- model
- language
- audio normalization on/off
- diarization on/off

When language mode is `Fixed`, the UI should present a predefined language selector rather than a free-text code field.
When an engine only supports a subset of fixed languages, the selector should be restricted to that engine-specific subset.

### Transcript view preferences
Controls how transcript content is displayed in the UI.

Includes:
- with timestamps
- without timestamps
- compact/readable mode

---

## 5. Information architecture

## Main application sections
- Dashboard
- Folders
- Folder Detail
- Project Detail
- Queue / Jobs
- Diagnostics
- Settings

## Suggested routes
- `/` -> Dashboard
- `/folders` -> All folders
- `/projects` -> All projects
- `/folders/:folderId` -> Folder detail
- `/projects/:projectId` -> Project detail
- `/queue` -> Queue / job monitoring
- `/diagnostics` -> Runtime diagnostics
- `/settings` -> Global defaults/settings

---

## 6. Navigation and layout

## App shell
Use a desktop-friendly layout with:

### Left sidebar
Contains:
- app name/logo
- Dashboard
- Folders
- Queue
- Diagnostics
- Settings

Optional additional filters/shortcuts:
- All Projects
- Completed
- Failed

### Main content panel
Displays the selected screen.

### Top bar
Contains:
- current page title
- breadcrumbs when relevant
- contextual actions such as:
  - Create Folder
  - Upload Files
  - Retry Failed
  - Export

---

## 7. Functional requirements

## 7.1 Folder management

The user must be able to:

- View all folders
- Create a folder
- Rename a folder
- Choose a folder icon from a small curated MUI icon set during create/edit
- Search the available folder icons by name during create/edit
- Choose and later change a folder color during create/edit
- Delete a folder
- Open a folder and see its projects

### Folder list display
Each folder item should show:
- folder name
- selected folder icon/color styling
- number of projects
- last updated date if available
- total storage used if available

### Folder actions
- Open
- Rename
- Delete
- Upload into folder

### MVP constraints
- Folders are **single-level only**
- No nested folders in MVP

---

## 7.2 Upload and project creation

The user must be able to upload:
- audio files
- video files

## 7.3 Diagnostics page

The user must be able to open a diagnostics page that shows:
- current backend runtime CPU consumption
- current backend runtime memory consumption
- transcription engine availability based on the current runtime environment
- used disk space per project

The diagnostics page should:
- refresh automatically on an interval suitable for lightweight monitoring
- clearly separate available and unavailable engines
- show per-project storage in a table that includes at minimum project name, folder name, total size, and storage breakdown when available

### Upload entry points
- Upload button inside a folder
- Drag and drop anywhere inside the folder detail page
- No global upload button in MVP; uploads start from a folder context

### File selection methods
- Drag and drop
- Manual file picker

### Multi-file support
The user must be able to select and queue multiple files in a single action.

### Project auto-creation
When files are uploaded:
- one project is created per file
- project name is pre-populated from the original filename
- settings are pre-selected using global defaults

### Project naming behavior
- remove file extension when pre-filling project name
- keep original filename separately
- allow the user to edit project name before queueing

### Supported file types (frontend validation)
Audio:
- mp3
- wav
- m4a
- flac
- ogg

Video:
- mp4
- mkv
- mov
- webm

Frontend validation should be permissive enough for common formats and defer final validation to backend if needed.

---

## 7.3 Batch upload review UI

When files are selected, the app should open a modal or drawer for batch review.

### Batch review contents
- list of selected files
- editable project names
- folder destination
- shared settings section
- queue action

### Shared settings section
The user must be able to set:
- engine
- model
- language
- audio normalization on/off
- diarization on/off

These settings should apply to all selected files by default.

### Per-file editable fields
For each file:
- project name
- file name display
- optional remove-from-batch action

### Primary actions
- Queue all files
- Cancel

### UX expectation
Batch upload should be optimized for quickly queueing many files without repetitive manual work.

---

## 7.4 Project/job lifecycle

Projects should support these statuses:

- Draft
- Queued
- Preparing Media
- Transcribing
- Completed
- Failed
- Cancelled

### Status requirements
- Status must be visible in folder lists, queue page, and project detail
- Failed projects must show an error summary if available
- Failed projects should expose a Retry action
- Retry should allow the user to adjust engine/model/language settings before re-queueing, especially when the original engine is not available in the current runtime
- Queued/transcribing projects should show progress if available

---

## 7.5 Queue / jobs monitoring

The app must provide a dedicated queue page.

### Queue page requirements
Show:
- queued projects
- currently processing projects
- completed projects
- failed projects

### Each queue item should show
- project name
- folder name
- status
- progress if available
- file duration if known
- transcription time if known
- selected engine/model if available
- created date/time
- storage used if available

### Queue actions
- Open project
- Retry failed, with the ability to adjust transcription settings before re-queueing
- Cancel queued job if backend supports it
- Delete/cancelled cleanup later if backend supports it

---

## 7.6 Project detail workspace

This is the main reading and playback screen.

### Header area
Show:
- project title
- folder breadcrumb
- project status
- upload/created date
- media metadata if available
- settings used for transcription
- transcription timing metrics if available
- storage usage summary

If the backend provides diagnostic timing metrics, show them in a clearly secondary debug-style block rather than mixing them with the primary status/progress presentation.

### Main workspace layout
Use a vertical stack for the MVP project workspace:

#### Top section
Media player

#### Bottom section
Transcript viewer

---

## 7.7 Media playback

The project page must allow playback of the uploaded file.

If the uploaded source is video and the backend provides a derived audio preview, the project page should allow switching between:
- source video playback
- extracted/prepared audio playback

### Media player requirements
For audio and video:
- play button
- pause button
- stop button
- track progress bar
- current time / total duration
- playback speed control
- volume control

The track progress bar should support seeking backward and forward by dragging or clicking the timeline.

### Transcript/media relationship
If transcript segments include timestamps, clicking a timestamp should seek playback to that point.

This is highly desirable for MVP.

---

## 7.8 Transcript viewing

The transcript page must support:

- readable transcript view
- optional timestamps
- speaker labels if diarization exists
- copy transcript
- search within transcript

### Transcript view modes
Support at least:
- **Readable mode**
- **Timestamped mode**

#### Readable mode
- grouped paragraphs
- minimal timestamp noise
- optimized for reading/studying

#### Timestamped mode
- segment-based display
- explicit timestamps on each segment or line
- useful for review and export validation

### Transcript display toggles
The user should be able to:
- toggle timestamps on/off
- search transcript text
- copy full transcript
- optionally collapse/expand metadata section

### Storage visibility
The project page should surface:
- original file size
- derived transcript/export storage usage if available
- total storage currently used by the project workspace

### Empty/loading states
The transcript area must clearly show:
- waiting for processing
- processing in progress
- failed state
- completed state

---

## 7.9 Export

The app must support transcript export in:

- PDF
- Markdown (`.md`)
- TXT
- HTML

### Export behavior
Exports should be available from:
- Project detail page

Exports are not required from project-row overflow menus in MVP.

### Export menu
Provide a clear export dropdown/button with format choices.

### Export content expectations
All export formats should include:
- project title
- folder name if available
- original file name
- generated/processed date if available
- transcript body

### Format-specific expectations

#### TXT
- plain text transcript
- optionally with or without timestamps depending on current view or export option

#### Markdown
- suitable for notes/Obsidian-like usage
- heading metadata + transcript content

#### HTML
- readable styled transcript page

#### PDF
- print-friendly document

### Future recommendation (not required in MVP)
Consider later adding:
- SRT
- VTT

---

## 7.10 Settings page

The app must expose a global settings page for future uploads.

### Settings that must be configurable
- default engine
- default model
- default language
- default audio normalization
- default diarization
- default transcript display mode

### Settings behavior
- global defaults apply only to new uploads
- changing settings does not retroactively modify existing projects
- upload modal should start from global defaults but allow override per batch
- batch and retry flows should allow diarization to be enabled or disabled per request
- engine selectors in settings, upload, retry, diagnostics, and model management should surface runtime-available engines from the backend, including separate Intel options such as `WhisperNetOpenVino` and `OpenVinoGenAi` when those runtimes are installed
- the settings page should also expose a model manager below the defaults form in a vertical stack layout
- the model manager should show known engine/model combinations, local install state, install path, and the latest probe result
- installed models should be probed on page load so runtime problems are visible without queueing an upload
- the user should be able to `Download`, `Redownload`, and `Probe` from the settings page
- the model manager should present the curated `OpenVinoGenAi` model set (`base-int8`, `small-fp16`, `tiny-int8`) when that engine is registered

---

## 8. UX requirements

## 8.1 General UX principles
- Keep flows simple and obvious
- Prefer clarity over dense enterprise-style controls
- Optimize for desktop first
- Make batch upload a first-class interaction
- Make transcript reading comfortable for long content

## 8.2 MVP no-auth behavior
- No login screen
- App opens directly to dashboard/folders
- No user profile/account menu required

## 8.3 Upload UX
- Drag-and-drop target should be visually clear, with the folder detail page becoming the active drop target when files are dragged over it
- Manual file selection should always be available
- Batch review should be fast and low-friction
- Project names should be editable before queue

## 8.4 Study-friendly project page
- Media and transcript should be visible together
- Transcript should be easy to scan
- Timestamp clicking should help navigate playback
- Search should be immediate and clear

## 8.5 Error handling UX
- Failed jobs must not disappear silently
- Show a visible failed state
- Expose retry action
- Show concise error message when available

---

## 9. Visual/UI requirements

## 9.1 Design system
Use:
- React
- MUI
- MUI icons
- MUI layout primitives
- MUI dialogs/drawers/menus/tables/cards/chips

## 9.2 Visual style
Target style:
- clean
- modern
- functional
- minimal clutter
- desktop productivity feel
- easy to read during long sessions

## 9.3 Suggested MUI components
- `AppBar`
- `Drawer`
- `List`
- `ListItemButton`
- `Card`
- `CardContent`
- `Button`
- `IconButton`
- `Chip`
- `Dialog`
- `Drawer` (right-side upload drawer is acceptable)
- `Menu`
- `Tabs` or segmented controls if useful
- `TextField`
- `Select`
- `FormControl`
- `Switch`
- `Checkbox`
- `LinearProgress`
- `Snackbar`
- `Alert`
- `Breadcrumbs`
- `Tooltip`

## 9.4 Accessibility
Minimum expectations:
- keyboard reachable major actions
- visible focus states
- semantic labels for actions/icons
- reasonable contrast
- transcript text readable at standard desktop sizes

---

## 10. Suggested page-level requirements

## 10.1 Dashboard
Purpose:
- quick overview

Should include:
- folder count
- total projects
- queued count
- processing count
- completed count
- failed count
- recent projects list
- quick actions:
  - Create Folder
  - Upload

This page can be simple in MVP.

---

## 10.2 Folders page
Purpose:
- browse all folders

Should include:
- folder grid or list
- create folder button
- rename/delete actions
- upload shortcut per folder
- icon and color customization when creating or editing a folder

---

## 10.3 Folder detail page
Purpose:
- manage projects inside a folder

Should include:
- folder title
- selected folder icon/color treatment in the page header
- folder storage summary if available
- upload area
- project list/table
- search/filter/sort controls if simple enough
- project status chips
- open project action
- quick export if completed

### Folder page primary CTA
- Upload files into this folder

---

## 10.4 Project detail page
Purpose:
- main transcript workspace

Should include:
- breadcrumb
- title
- rename project action
- status chip
- metadata summary
- storage usage summary
- media player displayed above the transcript viewer
- transcript viewer
- transcript search
- transcript mode toggle
- export menu
- retry action if failed
- loading/progress state if processing

---

## 10.5 Queue page
Purpose:
- operational visibility into all jobs

Should include:
- sections or tabs by status
- an optional `All` tab that aggregates every queue section into one operational view
- progress bars where possible
- quick actions for retry/open
- storage usage when available

---

## 10.6 Settings page
Purpose:
- manage defaults

Should include:
- form controls for defaults
- save/reset actions
- helper text describing effect on future uploads only

---

## 11. Empty states, loading states, and failure states

## 11.1 Empty states

### No folders
Show:
- a friendly empty state
- CTA to create first folder

### Empty folder
Show:
- folder is empty
- CTA to upload files

### No transcript yet
Show:
- queued or processing placeholder
- expected next status information if available

### Storage unavailable
Show a clear fallback when storage usage is unavailable or still being calculated.

## 11.2 Loading states
Use:
- skeletons for pages/lists where appropriate
- progress indicators for uploads and jobs
- clear busy states on buttons during submission

## 11.3 Failure states
Show:
- failed chip/status
- concise error message
- retry action

---

## 12. Notifications and feedback

Use transient UI feedback for:
- folder created
- folder renamed
- folder deleted
- files queued
- project updated
- export requested/completed if frontend handles it
- failure actions

Recommended component:
- `Snackbar` with `Alert`

---

## 13. State management expectations

The frontend architecture must support:

- server-fetched folder/project/job data
- optimistic or semi-optimistic UI where appropriate
- polling or refetching for queue/project status

Required practical approach:
- SWR for server state
- local component state for dialogs/forms

---

## 14. Data contracts and frontend assumptions

The frontend should be designed assuming backend APIs will provide:

### Folder data
- id
- name
- projectCount
- createdAt
- updatedAt
- storage usage summary if available

### Project data
- id
- folderId
- name
- originalFileName
- status
- mediaType
- mediaUrl
- createdAt
- updatedAt
- duration
- transcriptionElapsedMs when available for engine comparison
- progress
- settings summary
- transcript summary if completed
- storage usage summary

### Transcript data
- plain text
- structured segments
- timestamps
- speaker labels if available
- export availability

### Global settings data
- default engine
- default model
- default language
- default audio normalization
- default diarization
- default transcript display mode

The UI should not hardcode backend implementation details beyond these domain expectations.

---

## 15. MVP component inventory

Suggested components:

### Shell/navigation
- `AppShell`
- `SidebarNav`
- `TopBar`

### Folder components
- `FolderList`
- `FolderCard`
- `CreateFolderDialog`
- `RenameFolderDialog`

### Upload components
- `UploadDropzone`
- `UploadBatchDialog` or `UploadBatchDrawer`
- `BatchFileList`
- `TranscriptionSettingsForm`

### Project components
- `ProjectTable`
- `ProjectStatusChip`
- `ProjectMetadataPanel`
- `StorageUsageSummary`
- `MediaPlayer`
- `TranscriptViewer`
- `TranscriptSearchBar`
- `TranscriptToolbar`
- `ExportMenu`

### Queue components
- `JobList`
- `JobProgressRow`

### Settings components
- `DefaultSettingsForm`

---

## 16. Suggested frontend folder structure

```text
src/
  app/
    routes/
    layout/
    providers/
  pages/
    DashboardPage.tsx
    FoldersPage.tsx
    FolderDetailPage.tsx
    ProjectDetailPage.tsx
    QueuePage.tsx
    SettingsPage.tsx
  components/
    shell/
    folders/
    uploads/
    projects/
    queue/
    settings/
    common/
  api/
    folders.ts
    projects.ts
    jobs.ts
    settings.ts
  hooks/
  types/
  utils/
```

This is a recommendation, not a strict requirement.

---

## 17. Technical frontend constraints

- Use React
- Use MUI
- Frontend and backend run in the same Docker image at different ports
- No authentication required in MVP
- Design for desktop-first responsive behavior
- Keep implementation practical and maintainable
- Do not over-engineer the MVP

---

## 18. Acceptance criteria

The MVP frontend is acceptable when a user can:

1. Open the app with no login
2. Create folders such as Biology and Math
3. Open a folder
4. Drag and drop or manually select multiple files
5. Review and adjust batch settings
6. Queue files and see one project per file
7. Monitor progress in queue or project views
8. Open a completed project
9. Play audio/video in the browser
10. Read transcript in readable or timestamped mode
11. Search transcript text
12. Export transcript as PDF, MD, TXT, or HTML
13. View storage usage for folders and completed or in-progress project workspaces when available
14. Modify global defaults for future uploads

---

## 19. Future enhancements (not MVP)

- Nested folders
- Tags
- Search across all transcripts
- Speaker color coding and diarization improvements
- Transcript editing
- Summaries and note extraction
- SRT/VTT export
- Re-transcribe with new settings
- Bulk export
- Tailscale-aware share links
- Dark mode customization
- Keyboard transcript navigation shortcuts

---

## 20. Codex implementation notes

The frontend agent should prioritize:

1. Clear app structure
2. Good batch upload UX
3. A useful project detail workspace
4. Robust loading/empty/error states
5. Maintainable component boundaries

The frontend agent should avoid spending excessive effort on:
- premature abstraction
- overcomplicated design systems
- auth flows
- non-MVP collaboration/sharing features

The desired outcome is a polished, practical MVP frontend for TranscriptLab Nova.
