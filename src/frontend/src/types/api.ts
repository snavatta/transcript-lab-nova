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

export interface FolderDetailDto {
  id: string;
  name: string;
  iconKey: string;
  colorHex: string;
  projectCount: number;
  totalSizeBytes: number | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface CreateFolderRequest {
  name: string;
  iconKey?: string;
  colorHex?: string;
}

export interface UpdateFolderRequest {
  name: string;
  iconKey?: string;
  colorHex?: string;
}

export interface ProjectSettingsDto {
  engine: string;
  model: string;
  languageMode: LanguageMode;
  languageCode: string | null;
  audioNormalizationEnabled: boolean;
  diarizationEnabled: boolean;
  diarizationMode: string;
}

export interface UpdateProjectRequest {
  name: string;
}

export interface RetryProjectRequest {
  settings?: ProjectSettingsDto | null;
}

export interface ProjectDebugTimingsDto {
  totalElapsedMs: number | null;
  preparationElapsedMs: number | null;
  inspectElapsedMs: number | null;
  extractElapsedMs: number | null;
  normalizeElapsedMs: number | null;
  transcriptionElapsedMs: number | null;
  persistElapsedMs: number | null;
  transcriptionRealtimeFactor: number | null;
  totalRealtimeFactor: number | null;
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
  transcriptionElapsedMs: number | null;
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
  audioPreviewUrl?: string | null;
  transcriptAvailable: boolean;
  availableExports: string[];
  originalFileSizeBytes: number | null;
  workspaceSizeBytes: number | null;
  debugTimings?: ProjectDebugTimingsDto | null;
}

export interface QueueItemDto extends ProjectSummaryDto {
  engine: string;
  model: string;
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
  defaultDiarizationMode: string;
  defaultTranscriptViewMode: TranscriptViewMode;
}

export interface TranscriptionEngineOptionDto {
  engine: string;
  models: string[];
}

export interface TranscriptionOptionsDto {
  engines: TranscriptionEngineOptionDto[];
}

export interface TranscriptionModelEntryDto {
  engine: string;
  model: string;
  isInstalled: boolean;
  installPath: string | null;
  canDownload: boolean;
  canRedownload: boolean;
  canProbe: boolean;
  probeState: string;
  probeMessage: string;
}

export interface TranscriptionModelCatalogDto {
  models: TranscriptionModelEntryDto[];
}

export interface ManageTranscriptionModelRequest {
  engine: string;
  model: string;
  action: string;
}

export interface RuntimeDiagnosticsDto {
  collectedAtUtc: string;
  processId: number;
  processorCount: number;
  uptimeMs: number;
  cpuUsagePercent: number;
  workingSetBytes: number;
  privateMemoryBytes: number;
  managedHeapBytes: number;
}

export interface DiagnosticsEngineDto {
  engine: string;
  isAvailable: boolean;
  models: string[];
  availabilityError: string | null;
}

export interface ProjectStorageDiagnosticsDto {
  projectId: string;
  folderId: string;
  folderName: string;
  projectName: string;
  status: ProjectStatus;
  originalFileSizeBytes: number | null;
  workspaceSizeBytes: number | null;
  totalSizeBytes: number | null;
  updatedAtUtc: string;
}

export interface DiagnosticsDto {
  runtime: RuntimeDiagnosticsDto;
  engines: DiagnosticsEngineDto[];
  projects: ProjectStorageDiagnosticsDto[];
}

export interface UpdateGlobalSettingsRequest {
  defaultEngine: string;
  defaultModel: string;
  defaultLanguageMode: LanguageMode;
  defaultLanguageCode: string | null;
  defaultAudioNormalizationEnabled: boolean;
  defaultDiarizationEnabled: boolean;
  defaultDiarizationMode: string;
  defaultTranscriptViewMode: TranscriptViewMode;
}

export interface BatchUploadResultDto {
  folderId: string;
  createdProjects: ProjectSummaryDto[];
}

export interface ErrorResponse {
  code: string;
  message: string;
  details?: Record<string, string[]>;
}
