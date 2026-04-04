import useSWR from 'swr';
import { diagnosticsApi, foldersApi, projectsApi, queueApi, settingsApi } from '../api';
import type { ProjectListParams } from '../api/projects';
import type { ProjectDetailDto, ProjectSummaryDto } from '../types';

const ACTIVE_PROJECT_REFRESH_INTERVAL_MS = 3000;

const ACTIVE_STATUSES = ['Queued', 'PreparingMedia', 'Transcribing'] as const;
type ActiveStatus = typeof ACTIVE_STATUSES[number];

function isActiveStatus(status: string): status is ActiveStatus {
  return (ACTIVE_STATUSES as readonly string[]).includes(status);
}

function shouldRefreshProject(project?: ProjectDetailDto) {
  return project != null && isActiveStatus(project.status);
}

function shouldRefreshProjects(projects?: ProjectSummaryDto[]) {
  return projects != null && projects.some((p) => isActiveStatus(p.status));
}

export function useFolders() {
  return useSWR('folders', () => foldersApi.list());
}

export function useFolder(id: string | undefined) {
  return useSWR(id ? `folders/${id}` : null, () => foldersApi.getById(id!));
}

export function useProjects(params: ProjectListParams = {}) {
  const key = `projects?${JSON.stringify(params)}`;
  return useSWR(key, () => projectsApi.list(params), {
    refreshInterval: (projects) => (
      shouldRefreshProjects(projects)
        ? ACTIVE_PROJECT_REFRESH_INTERVAL_MS
        : 0
    ),
  });
}

export function useProject(id: string | undefined) {
  return useSWR(
    id ? `projects/${id}` : null,
    () => projectsApi.getById(id!),
    {
      refreshInterval: (project) => (
        shouldRefreshProject(project)
          ? ACTIVE_PROJECT_REFRESH_INTERVAL_MS
          : 0
      ),
    },
  );
}

export function useTranscript(projectId: string | undefined, enabled: boolean) {
  return useSWR(
    projectId && enabled ? `projects/${projectId}/transcript` : null,
    () => projectsApi.getTranscript(projectId!),
  );
}

export function useQueue() {
  return useSWR('queue', () => queueApi.overview(), { refreshInterval: 5000 });
}

export function useSettings() {
  return useSWR('settings', () => settingsApi.get());
}

export function useTranscriptionOptions() {
  return useSWR('settings/options', () => settingsApi.getOptions());
}

export function useTranscriptionModels() {
  return useSWR('settings/models', () => settingsApi.getModels());
}

export function useDiagnostics() {
  return useSWR('diagnostics', () => diagnosticsApi.get(), { refreshInterval: 5000 });
}
