# Backend Tech Stack Requirements

## Project Baseline
- **.NET 10 LTS** - Required local and CI runtime
- **ASP.NET Core Web API** - Required HTTP API framework
- **C# latest supported by .NET 10** - Standard backend language version
- **Nullable reference types** - Must remain enabled for all application code
- **Implicit usings** - Allowed for standard SDK ergonomics
- **Docker** - Required deployment target for local and homelab environments
- **Linux-first runtime support** - Backend must run correctly in Linux containers
- Public container distribution may publish separate CPU, CUDA, and OpenVino GenAI image variants when they remain behaviorally equivalent aside from runtime acceleration dependencies

## Homelab Hardware Assumptions
- Target host baseline: **Intel i3-12100KF**, **16 GB RAM**, **Intel Arc A310 4 GB**
- Default transcription concurrency should be **1**
- Higher concurrency must be configurable and opt-in, not the default
- GPU acceleration should be optional and capability-checked rather than assumed
- MVP model selection should remain conservative and aligned with limited VRAM
- Temporary media processing and export generation should minimize unnecessary disk churn on the host
- Storage paths, worker concurrency, FFmpeg location, and transcription runtime options must be configurable

## API Framework
- **ASP.NET Core Minimal APIs** - Required HTTP API surface
- Do not mix controller-based and minimal-API endpoint styles in MVP
- **System.Text.Json** - Standard JSON serializer for request/response contracts
- **OpenAPI/Swagger** - Required for local contract inspection and backend development
- **ModelContextProtocol.AspNetCore 2.1.0** - Approved only for the opt-in,
  in-process, read-only private MCP transcript source defined by the shared
  contract; its version must be centrally pinned
- The MCP source uses stateless Streamable HTTP at exactly `/mcp`; legacy MCP
  SSE transport is disabled. This does not change the separately approved SSE
  pattern for OpenVino sidecar model-download progress.

## Application Architecture
- **Single deployable ASP.NET Core application** - Required MVP architecture
- **Service-oriented application layer** - Business logic should not live in endpoint handlers
- **BackgroundService** - Required hosted worker model for queued transcription jobs
- **Options pattern** - Standard configuration binding approach
- **Dependency injection via built-in container** - Standard service registration mechanism

## Persistence
- **Entity Framework Core** - Standard ORM/data access approach
- **SQLite** - Required relational database for MVP persistence
- **EF Core migrations** - Required schema management mechanism
- Store large media and export files on disk, not in database blobs

## File and Media Processing
- **Local filesystem storage** - Required storage mechanism for uploads, prepared audio, and exports
- **FFmpeg** - Standard external media tool for inspection, extraction, and audio preparation
- Wrap FFmpeg usage behind internal abstractions such as media inspector/extractor/normalizer services
- Generate safe stored file names and constrain all storage operations to configured base paths

## Background Processing
- **Database-backed project status workflow** - Required durable job coordination mechanism
- Start with sequential processing or very low configurable concurrency
- Queue orchestration should remain inside the application; do not introduce external brokers for MVP
- **BackgroundService** - Required default execution model for transcription job processing
- **Hangfire** - Not approved as the primary MVP job system; may be reconsidered later if an operational dashboard and richer persistent job orchestration become necessary
- **Quartz.NET** - Not approved as the primary MVP job system; may be introduced later only for recurring or schedule-driven maintenance tasks if those requirements emerge

## Transcription Integration
- **Pluggable transcription engine abstraction** - Required integration boundary
- **WhisperNet-based implementation** - Required default MVP engine family
- **SherpaOnnx** via the official local **.NET runtime/package** is approved behind the engine abstraction; running it through an isolated helper worker process is allowed when needed for cancellation or runtime isolation
- **Whisper.net** managed library with **Whisper.net.Runtime** (CPU), **Whisper.net.Runtime.Cuda** (NVIDIA GPU), and **Whisper.net.Runtime.CoreML** (native macOS Apple Silicon) runtimes are approved behind the engine abstraction, but CPU, CUDA, and CoreML execution must run through isolated helper worker processes because Whisper.net runtime loading is process-global. CoreML is only approved for native macOS ARM64 runs; Docker/Linux on macOS should be treated as CPU-only unless it calls a native host sidecar.
- A separate **Python FastAPI sidecar** backed by **openvino-genai**, **fastapi**, and **uvicorn** is approved for the `OpenVinoWhisperSidecar` engine; the sidecar runs as a long-lived localhost HTTP server managed by the API process (spawned lazily on first use, killed on shutdown), caches loaded Whisper models in memory between jobs, and avoids the native library version conflict between the .NET `Whisper.net.Runtime.OpenVino` binding and newer OpenVINO Python package installs; the sidecar exposes an OpenAI-compatible `/v1/audio/transcriptions` endpoint plus a model management API with SSE-streamed download progress; it manages its own model downloads independently of the C# download infrastructure; the C# engine communicates with it via `ISpeechToTextClient` from `Microsoft.Extensions.AI.Abstractions`
- **Microsoft.Extensions.AI.Abstractions** is approved as an internal calling abstraction for HTTP-based transcription engines (`OpenVinoWhisperSidecar`, `OpenAiCompatible`, `OpenRouter`); it is used as an implementation detail within the engine and must not replace `IRegisteredTranscriptionEngine` as the public engine contract; the `MEAI001` experimental diagnostic should be suppressed project-wide via `<NoWarn>` in the `.csproj` file when the package is added
- A generic **`OpenAiCompatible`** proxy engine is approved that forwards transcription requests to any OpenAI-compatible `/v1/audio/transcriptions` endpoint; it shares HTTP multipart construction code with the `OpenVinoWhisperSidecar` client and is hidden from the engine selector when `BaseUrl` is not configured
- A first-class **`OpenRouter`** engine is approved for hosted speech-to-text through `/api/v1/audio/transcriptions`; it reuses the shared OpenAI-compatible multipart helper, authenticates only with a server-side API key, discovers models with `GET /api/v1/models?output_modalities=transcription`, uses the project-selected model, and stays outside local model download/install management. Ordinary discovery remains dynamic. OpenRouter word-timestamp long-form mode is limited to exactly `openai/whisper-large-v3` and `openai/whisper-large-v3-turbo`; it sends sequential 600-second FLAC cores with up to two seconds of overlap, requires every encoded part to be strictly below 24,000,000 bytes, checkpoints each successful chunk, and retries only 429/503 for at most three total attempts honoring `Retry-After`. Provider timeouts are fatal and are not retried.
- A first-class **`Xai`** engine is approved for direct whole-file speech-to-text through `/v1/stt`; it uses server-side bearer authentication, one whole lossless FLAC, native word timestamps and diarization, 500 MB preflight, bounded 429/503 retries, and never automatically chunks or switches providers. Explicit xAI timing diarization runs only after compatible OpenRouter words exist; failure is fatal rather than a fallback trigger.
- Hosted monetary values are persisted as integer micro-USD. Direct xAI STT uses an estimated configured-rate snapshot; OpenRouter usage costs remain actual. API totals are checked sums of STT, diarization, and role-attribution costs without double counting native diarization.
- An **`OnnxWhisper`** engine placeholder value is approved in the `TranscriptionEngine` enum; do not add `Microsoft.ML.OnnxRuntime` or any ONNX inference package until the full implementation is planned
- SSE (Server-Sent Events) streaming is the approved pattern for long-running sidecar model download progress; the C# caller must consume the SSE stream until `status=complete` or `status=error`
- Keep engine-specific logic behind a dedicated transcription service and engine interface
- Speaker diarization has an explicit source: `Local` runs the selected `Basic` or `Improved` post-processing mode, `Provider` consumes native speaker metadata and skips local diarization, and `Xai` means compatible OpenRouter wording plus a whole-FLAC xAI timing merge. Engine options advertise native and word-timestamp capability per model; currently only direct `Xai` with `grok-stt-1.0` advertises native provider diarization.

## Logging and Observability
- **Microsoft.Extensions.Logging** - Baseline logging abstraction
- **Serilog** - Standard structured logging implementation
- Log uploads, queue transitions, transcription lifecycle events, export generation, and failure paths
- Maintain a practical per-project correlation path where possible
- For MCP, log only lifecycle and sanitized outcome metadata. Never log raw
  queries, transcript text, tool results, private paths or configured URLs,
  client identifiers, API keys, credentials, or unredacted evidence.

## Configuration
- **appsettings.json + environment-specific overrides + environment variables** - Standard configuration sources
- Use typed options for storage paths, database connection, FFmpeg location, and transcription settings
- Expose typed options for worker concurrency, Sherpa runtime settings, Whisper.net worker path/host settings, and optional GPU/transcription runtime settings
- Optional debug-oriented worker logging such as per-segment transcription logs must be configurable and disabled by default
- Secrets should come from environment variables or deployment configuration, not committed files
- Local development defaults should keep runtime data outside tracked source directories
- `McpOptions` typed options, bound to the `Mcp` configuration section, are
  approved for the optional MCP source:
  `Enabled=false` by default, nullable `ApplicationBaseUrl`, and exactly one
  enabled-mode cursor-integrity key source:
  `CursorIntegrityKey` or `CursorIntegrityKeyFile`. The resolved key is strict
  UTF-8, 32 through 4096 bytes, without BOM, NUL, carriage return, line feed,
  or whitespace-only content. A key file is bounded to 4099 raw bytes and may
  have one final `LF` or `CRLF` only. Invalid key configuration must fail with
  the sanitized stable configuration error without exposing the key or path.
  The key source and external-client settings must not be committed.

## HTTP and Contract Rules
- Use the shared contract in `class-transcriber-shared-api-contract.md` as the source of truth for DTOs and route behavior
- Return DTOs/contracts rather than EF Core entities
- Use UTC timestamps in all persisted and API-exposed date fields
- Keep error responses in a consistent structured JSON format
- `/mcp` is the documented exception to the `/api` REST convention. It is not a
  REST endpoint and must remain disabled unless `Mcp:Enabled` is true.
- When enabled in container deployment, `/mcp` is accepted only on its dedicated
  private listener, published as `127.0.0.1:5001:5001`; port 5000 remains the
  UI/REST/health listener and must reject `/mcp`. Do not bundle or manage an
  external client, add LAN/public MCP access, or store its credentials or state
  in application settings.

## Development Tools
- **dotnet CLI** - Standard local and CI build/test toolchain
- **OpenAPI/Swagger UI** - Required local API exploration tool
- **EditorConfig + analyzers** - Required code-style and correctness baseline
- **Nullable warnings and standard compiler warnings** should be treated as issues to resolve, not ignore

## Testing
- **xUnit** - Standard test framework
- **FluentAssertions** - Standard assertion library
- **ASP.NET Core WebApplicationFactory** - Standard integration-test host
- **SQLite test database or isolated test database file** - Preferred persistence test approach
- Cover critical flows with automated tests:
- folder CRUD
- batch upload validation
- project creation and queue state transitions
- transcript retrieval behavior
- export endpoint behavior
- retry behavior

## Security and Runtime Safety
- Validate file paths, file names, and request payloads defensively even without authentication
- Do not trust client-provided file names, media types, or extensions
- Configure CORS explicitly for development when frontend and backend run on different local ports
- Prefer same-origin deployment behavior in the final Dockerized setup where practical
- The MCP source is read-only and private. It exposes only the four tools in the
  shared contract, treats transcript content as untrusted source material, and
  must not execute instructions or links from that content. MCP error text must
  omit transcript/query/private-path/configuration/credential data.
- MCP search is literal and folds only ASCII `A` through `Z` to `a` through `z`
  per UTF-16 code unit; all other code units match exactly, with no Unicode case
  folding or normalization. MCP provenance `sourcePath` is the origin-rooted
  application path exactly `/projects/{projectId}`, never a filesystem path.

## Approved Libraries
- **Microsoft.EntityFrameworkCore.Sqlite** - SQLite provider
- **Microsoft.EntityFrameworkCore.Design** - Migration/design-time support
- **Serilog.AspNetCore** - Structured request/application logging
- **Swashbuckle.AspNetCore** - Standard OpenAPI package
- **ModelContextProtocol.AspNetCore 2.1.0** - Pinned MCP server transport

## Library Policy
- Prefer built-in .NET platform features before introducing third-party infrastructure libraries
- Add external packages only when they materially simplify implementation or reliability
- Do not introduce Redis, message brokers, MediatR, AutoMapper, or distributed job systems for MVP unless requirements materially change
- Do not replace the project-status-driven transcription workflow with Hangfire or Quartz in MVP
