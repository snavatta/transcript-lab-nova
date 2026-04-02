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

export interface QueueItemDto extends ProjectSummaryDto {
  engine: string;
  model: string;
}

export interface QueueOverviewDto {
  drafts: QueueItemDto[];
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

export interface TranscriptionEngineOptionDto {
  engine: string;
  models: string[];
}

export interface TranscriptionOptionsDto {
  engines: TranscriptionEngineOptionDto[];
}

export interface UpdateGlobalSettingsRequest {
  defaultEngine: string;
  defaultModel: string;
  defaultLanguageMode: LanguageMode;
  defaultLanguageCode: string | null;
  defaultAudioNormalizationEnabled: boolean;
  defaultDiarizationEnabled: boolean;
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
