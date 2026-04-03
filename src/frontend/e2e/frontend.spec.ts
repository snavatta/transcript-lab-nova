import { Buffer } from 'node:buffer';
import { expect, test, type Page, type Route } from '@playwright/test';

interface FolderState {
  id: string;
  name: string;
  iconKey: string;
  colorHex: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

interface ProjectState {
  id: string;
  folderId: string;
  name: string;
  originalFileName: string;
  status: 'Queued' | 'Completed';
  progress: number | null;
  mediaType: 'Audio' | 'Video';
  durationMs: number | null;
  transcriptionElapsedMs: number | null;
  totalSizeBytes: number | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  settings: {
    engine: string;
    model: string;
    languageMode: 'Auto' | 'Fixed';
    languageCode: string | null;
    audioNormalizationEnabled: boolean;
    diarizationEnabled: boolean;
  };
  transcriptAvailable: boolean;
  availableExports: string[];
  originalFileSizeBytes: number | null;
  workspaceSizeBytes: number | null;
  detailRequests: number;
}

interface MockState {
  folders: FolderState[];
  projects: ProjectState[];
  settings: {
    defaultEngine: string;
    defaultModel: string;
    defaultLanguageMode: 'Auto' | 'Fixed';
    defaultLanguageCode: string | null;
    defaultAudioNormalizationEnabled: boolean;
    defaultDiarizationEnabled: boolean;
    defaultTranscriptViewMode: 'Readable' | 'Timestamped';
  };
}

const NOW = '2026-04-02T12:00:00Z';

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify(body),
  });
}

function createMockState(): MockState {
  return {
    folders: [],
    projects: [],
    settings: {
      defaultEngine: 'WhisperNet',
      defaultModel: 'small',
      defaultLanguageMode: 'Auto',
      defaultLanguageCode: null,
      defaultAudioNormalizationEnabled: true,
      defaultDiarizationEnabled: false,
      defaultTranscriptViewMode: 'Timestamped',
    },
  };
}

function getFolderProjectCount(state: MockState, folderId: string) {
  return state.projects.filter((project) => project.folderId === folderId).length;
}

function getFolderTotalSize(state: MockState, folderId: string) {
  return state.projects
    .filter((project) => project.folderId === folderId)
    .reduce((total, project) => total + (project.totalSizeBytes ?? 0), 0);
}

function toFolderSummary(state: MockState, folder: FolderState) {
  return {
    id: folder.id,
    name: folder.name,
    iconKey: folder.iconKey,
    colorHex: folder.colorHex,
    projectCount: getFolderProjectCount(state, folder.id),
    totalSizeBytes: getFolderTotalSize(state, folder.id),
    createdAtUtc: folder.createdAtUtc,
    updatedAtUtc: folder.updatedAtUtc,
  };
}

function toProjectSummary(state: MockState, project: ProjectState) {
  const folder = state.folders.find((entry) => entry.id === project.folderId);

  return {
    id: project.id,
    folderId: project.folderId,
    folderName: folder?.name ?? 'Unknown',
    name: project.name,
    originalFileName: project.originalFileName,
    status: project.status,
    progress: project.progress,
    mediaType: project.mediaType,
    durationMs: project.durationMs,
    transcriptionElapsedMs: project.transcriptionElapsedMs,
    totalSizeBytes: project.totalSizeBytes,
    createdAtUtc: project.createdAtUtc,
    updatedAtUtc: project.updatedAtUtc,
  };
}

function toProjectDetail(state: MockState, project: ProjectState) {
  return {
    ...toProjectSummary(state, project),
    queuedAtUtc: project.createdAtUtc,
    startedAtUtc: project.status === 'Completed' ? NOW : null,
    completedAtUtc: project.status === 'Completed' ? NOW : null,
    failedAtUtc: null,
    errorMessage: null,
    settings: project.settings,
    mediaUrl: `/api/projects/${project.id}/media`,
    transcriptAvailable: project.transcriptAvailable,
    availableExports: project.availableExports,
    originalFileSizeBytes: project.originalFileSizeBytes,
    workspaceSizeBytes: project.workspaceSizeBytes,
  };
}

function completeProject(project: ProjectState) {
  project.status = 'Completed';
  project.progress = 100;
  project.durationMs = 3_600_000;
  project.transcriptionElapsedMs = 522_000;
  project.totalSizeBytes = 412_345_678;
  project.updatedAtUtc = NOW;
  project.transcriptAvailable = true;
  project.availableExports = ['txt', 'md', 'html', 'pdf'];
  project.originalFileSizeBytes = 385_000_000;
  project.workspaceSizeBytes = 27_345_678;
}

async function installMockApi(page: Page) {
  const state = createMockState();

  await page.route('http://127.0.0.1:4173/api/**', async (route) => {
    const url = new URL(route.request().url());
    const method = route.request().method();
    const { pathname, searchParams } = url;

    if (pathname === '/api/settings' && method === 'GET') {
      return json(route, state.settings);
    }

    if (pathname === '/api/settings' && method === 'PUT') {
      const body = route.request().postDataJSON() as MockState['settings'];
      state.settings = body;
      return json(route, state.settings);
    }

    if (pathname === '/api/folders' && method === 'GET') {
      return json(route, state.folders.map((folder) => toFolderSummary(state, folder)));
    }

    if (pathname === '/api/folders' && method === 'POST') {
      const body = route.request().postDataJSON() as { name: string; iconKey?: string; colorHex?: string };
      const folder: FolderState = {
        id: `folder-${state.folders.length + 1}`,
        name: body.name,
        iconKey: body.iconKey ?? 'Folder',
        colorHex: body.colorHex ?? '#546E7A',
        createdAtUtc: NOW,
        updatedAtUtc: NOW,
      };
      state.folders.push(folder);
      return json(route, toFolderSummary(state, folder));
    }

    if (pathname.startsWith('/api/folders/') && method === 'GET') {
      const folderId = pathname.replace('/api/folders/', '');
      const folder = state.folders.find((entry) => entry.id === folderId);

      if (!folder) {
        return json(route, { code: 'not_found', message: 'Folder not found.' }, 404);
      }

      return json(route, toFolderSummary(state, folder));
    }

    if (pathname.startsWith('/api/folders/') && method === 'PUT') {
      const folderId = pathname.replace('/api/folders/', '');
      const folder = state.folders.find((entry) => entry.id === folderId);

      if (!folder) {
        return json(route, { code: 'not_found', message: 'Folder not found.' }, 404);
      }

      const body = route.request().postDataJSON() as { name: string; iconKey?: string; colorHex?: string };
      folder.name = body.name;
      folder.iconKey = body.iconKey ?? folder.iconKey;
      folder.colorHex = body.colorHex ?? folder.colorHex;
      folder.updatedAtUtc = NOW;

      return json(route, toFolderSummary(state, folder));
    }

    if (pathname === '/api/projects' && method === 'GET') {
      const folderId = searchParams.get('folderId');
      const search = searchParams.get('search')?.toLowerCase();
      const projects = state.projects.filter((project) => {
        if (folderId && project.folderId !== folderId) {
          return false;
        }

        if (search && !project.name.toLowerCase().includes(search)) {
          return false;
        }

        return true;
      });

      return json(route, projects.map((project) => toProjectSummary(state, project)));
    }

    if (pathname === '/api/uploads/batch' && method === 'POST') {
      const folder = state.folders[0];

      if (!folder) {
        return json(route, { code: 'validation_error', message: 'Folder is required.' }, 400);
      }

      const project: ProjectState = {
        id: 'project-1',
        folderId: folder.id,
        name: 'Biology Lecture 01',
        originalFileName: 'lecture01.mp3',
        status: 'Queued',
        progress: 0,
        mediaType: 'Audio',
        durationMs: null,
        transcriptionElapsedMs: null,
        totalSizeBytes: 268_435_456,
        createdAtUtc: NOW,
        updatedAtUtc: NOW,
        settings: {
          engine: state.settings.defaultEngine,
          model: state.settings.defaultModel,
          languageMode: state.settings.defaultLanguageMode,
          languageCode: state.settings.defaultLanguageCode,
          audioNormalizationEnabled: state.settings.defaultAudioNormalizationEnabled,
          diarizationEnabled: state.settings.defaultDiarizationEnabled,
        },
        transcriptAvailable: false,
        availableExports: [],
        originalFileSizeBytes: null,
        workspaceSizeBytes: null,
        detailRequests: 0,
      };

      state.projects = [project];

      return json(route, {
        folderId: folder.id,
        createdProjects: [toProjectSummary(state, project)],
      });
    }

    if (pathname === '/api/queue' && method === 'GET') {
      return json(route, {
        queued: state.projects.filter((project) => project.status === 'Queued').map((project) => ({
          ...toProjectSummary(state, project),
          engine: project.settings.engine,
          model: project.settings.model,
        })),
        processing: [],
        completed: state.projects.filter((project) => project.status === 'Completed').map((project) => ({
          ...toProjectSummary(state, project),
          engine: project.settings.engine,
          model: project.settings.model,
        })),
        failed: [],
      });
    }

    if (pathname.startsWith('/api/projects/') && pathname.endsWith('/transcript') && method === 'GET') {
      const projectId = pathname.replace('/api/projects/', '').replace('/transcript', '');
      const project = state.projects.find((entry) => entry.id === projectId);

      if (!project || !project.transcriptAvailable) {
        return json(route, { code: 'conflict', message: 'Transcript is not available yet.' }, 409);
      }

      return json(route, {
        projectId,
        plainText: 'Cell biology starts with the structure of the cell.',
        detectedLanguage: 'en',
        durationMs: project.durationMs,
        segmentCount: 2,
        segments: [
          {
            startMs: 0,
            endMs: 4200,
            text: 'Cell biology starts with the structure of the cell.',
            speaker: null,
          },
          {
            startMs: 5000,
            endMs: 9000,
            text: 'Then we compare prokaryotic and eukaryotic cells.',
            speaker: null,
          },
        ],
        createdAtUtc: NOW,
        updatedAtUtc: NOW,
      });
    }

    if (pathname.startsWith('/api/projects/') && pathname.endsWith('/media') && method === 'GET') {
      return route.fulfill({
        status: 200,
        contentType: 'audio/mpeg',
        body: 'mock-media',
      });
    }

    if (pathname.startsWith('/api/projects/') && pathname.endsWith('/export') && method === 'GET') {
      return route.fulfill({
        status: 200,
        contentType: 'text/plain',
        body: 'mock-export',
      });
    }

    if (pathname.startsWith('/api/projects/') && method === 'GET') {
      const projectId = pathname.replace('/api/projects/', '');
      const project = state.projects.find((entry) => entry.id === projectId);

      if (!project) {
        return json(route, { code: 'not_found', message: 'Project not found.' }, 404);
      }

      project.detailRequests += 1;
      if (project.detailRequests >= 2 && project.status === 'Queued') {
        completeProject(project);
      }

      return json(route, toProjectDetail(state, project));
    }

    if (pathname.startsWith('/api/projects/') && pathname.endsWith('/retry') && method === 'POST') {
      const projectId = pathname.replace('/api/projects/', '').replace('/retry', '');
      const project = state.projects.find((entry) => entry.id === projectId);

      if (!project) {
        return json(route, { code: 'not_found', message: 'Project not found.' }, 404);
      }

      project.status = 'Queued';
      project.progress = 0;
      project.transcriptAvailable = false;
      project.availableExports = [];
      project.detailRequests = 0;

      return json(route, toProjectDetail(state, project));
    }

    if (pathname.startsWith('/api/projects/') && pathname.endsWith('/cancel') && method === 'POST') {
      const projectId = pathname.replace('/api/projects/', '').replace('/cancel', '');
      const project = state.projects.find((entry) => entry.id === projectId);

      if (!project) {
        return json(route, { code: 'not_found', message: 'Project not found.' }, 404);
      }

      project.status = 'Completed';
      completeProject(project);

      return json(route, toProjectDetail(state, project));
    }

    return route.fulfill({ status: 404, body: 'Unhandled API route' });
  });
}

test('supports folder creation, upload review, queue monitoring, project polling, and export availability', async ({ page }) => {
  await installMockApi(page);

  await page.goto('/folders');

  await page.locator('header').getByRole('button', { name: 'Create Folder' }).click();
  await page.getByLabel('Folder name').fill('Biology');
  await page.getByRole('combobox', { name: 'Folder icon' }).fill('Science');
  await page.getByRole('option', { name: /^Science$/ }).click();
  await page.getByLabel('Color hex').fill('#2E7D32');
  await page.getByRole('button', { name: 'Create' }).click();

  await expect(page.getByText('Biology')).toBeVisible();
  await page.getByLabel('Folder actions').click();
  await page.getByRole('menuitem', { name: 'Edit' }).click();
  await expect(page.getByRole('combobox', { name: 'Folder icon' })).toHaveValue('Science');
  await expect(page.getByLabel('Color hex')).toHaveValue('#2E7D32');
  await page.getByRole('combobox', { name: 'Folder icon' }).fill('Biotech');
  await page.getByRole('option', { name: /^Biotech$/ }).click();
  await page.getByLabel('Color hex').fill('#8E24AA');
  await page.getByRole('button', { name: 'Save' }).click();

  await page.getByLabel('Folder actions').click();
  await page.getByRole('menuitem', { name: 'Edit' }).click();
  await expect(page.getByRole('combobox', { name: 'Folder icon' })).toHaveValue('Biotech');
  await expect(page.getByLabel('Color hex')).toHaveValue('#8E24AA');
  await page.getByRole('button', { name: 'Cancel' }).click();

  await page.getByText('Biology').click();

  await page.locator('input[type="file"]').setInputFiles([
    {
      name: 'lecture01.mp3',
      mimeType: 'audio/mpeg',
      buffer: Buffer.from('mock-audio'),
    },
  ]);

  await expect(page.getByRole('heading', { name: 'Upload Files' })).toBeVisible();
  await expect(page.getByRole('combobox').nth(0)).toHaveText('WhisperNet.CPU');
  await expect(page.getByRole('combobox').nth(1)).toHaveText('small');
  await page.getByRole('button', { name: 'Upload & Queue' }).click();

  await expect(page.getByText('Biology Lecture 01')).toBeVisible();

  await page.getByRole('button', { name: 'Queue' }).click();
  await page.getByRole('tab', { name: /Queued/ }).click();

  await expect(page.getByText('Biology Lecture 01')).toBeVisible();
  await expect(page.getByText('WhisperNet.CPU / small')).toBeVisible();

  await page.getByText('Biology Lecture 01').click();

  await expect(page.getByText('Waiting in queue')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Export' })).toBeVisible({ timeout: 20_000 });
  await expect(page.getByRole('button', { name: 'Timestamped' })).toHaveAttribute('aria-pressed', 'true');

  await page.getByRole('button', { name: 'Export' }).click();
  await expect(page.getByRole('menuitem', { name: 'Plain Text (.txt)' })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: 'Markdown (.md)' })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: 'HTML (.html)' })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: 'PDF (.pdf)' })).toBeVisible();
});
