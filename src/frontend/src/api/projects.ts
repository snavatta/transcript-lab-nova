import { get, post, del, downloadUrl } from './client';
import type {
  ProjectSummaryDto,
  ProjectDetailDto,
  TranscriptDto,
} from '../types';

export interface ProjectListParams {
  folderId?: string;
  status?: string;
  search?: string;
  sort?: string;
}

function buildQueryString(params: ProjectListParams): string {
  const searchParams = new URLSearchParams();
  if (params.folderId) searchParams.set('folderId', params.folderId);
  if (params.status) searchParams.set('status', params.status);
  if (params.search) searchParams.set('search', params.search);
  if (params.sort) searchParams.set('sort', params.sort);
  const qs = searchParams.toString();
  return qs ? `?${qs}` : '';
}

export const projectsApi = {
  list: (params: ProjectListParams = {}) =>
    get<ProjectSummaryDto[]>(`/projects${buildQueryString(params)}`),
  getById: (id: string) => get<ProjectDetailDto>(`/projects/${id}`),
  remove: (id: string) => del(`/projects/${id}`),
  queue: (id: string) => post<ProjectDetailDto>(`/projects/${id}/queue`),
  retry: (id: string) => post<ProjectDetailDto>(`/projects/${id}/retry`),
  cancel: (id: string) => post<ProjectDetailDto>(`/projects/${id}/cancel`),
  getTranscript: (id: string) => get<TranscriptDto>(`/projects/${id}/transcript`),
  mediaUrl: (id: string) => downloadUrl(`/projects/${id}/media`),
  exportUrl: (id: string, format: string, viewMode?: string, includeTimestamps?: boolean) => {
    const params = new URLSearchParams({ format });
    if (viewMode) params.set('viewMode', viewMode);
    if (includeTimestamps !== undefined) params.set('includeTimestamps', String(includeTimestamps));
    return downloadUrl(`/projects/${id}/export?${params.toString()}`);
  },
};
