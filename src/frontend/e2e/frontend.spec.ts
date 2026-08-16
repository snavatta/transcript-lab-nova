import { Buffer } from 'node:buffer';
import { expect, test, type Locator, type Page, type Route } from '@playwright/test';

const frontendPort = process.env.FRONTEND_PORT;
if (frontendPort === undefined) {
  throw new Error('Playwright did not capture the preview server port.');
}

test.use({ baseURL: `http://127.0.0.1:${frontendPort}` });

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
  status: 'Queued' | 'Completed' | 'Failed';
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
    diarizationSource: 'Local' | 'Provider' | 'Xai';
    diarizationMode: string;
    speakerRoleAttributionEnabled: boolean;
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
    defaultDiarizationSource: 'Local' | 'Provider' | 'Xai';
    defaultDiarizationMode: string;
    defaultSpeakerRoleAttributionEnabled: boolean;
    defaultTranscriptViewMode: 'Readable' | 'Timestamped';
  };
}

interface HostedProcessingMock {
  readonly sttProvider: string;
  readonly sttModel: string;
  readonly audioDurationMs: number | null;
  readonly requestCount: number;
  readonly nativeDiarizationUsed: boolean;
  readonly sttCostUsd: number | null;
  readonly sttRateUsdPerHour: number | null;
  readonly sttCostClassification: string | null;
  readonly diarizationSource: string | null;
  readonly diarizationProvider: string | null;
  readonly diarizationModel: string | null;
  readonly diarizationRequestCount: number | null;
  readonly diarizationCostUsd: number | null;
  readonly diarizationRateUsdPerHour: number | null;
  readonly diarizationCostClassification: string | null;
  readonly roleAttributionModel: string | null;
  readonly roleAttributionStatus: string | null;
  readonly roleAttributionPromptTokens: number | null;
  readonly roleAttributionOutputTokens: number | null;
  readonly roleAttributionCostUsd: number | null;
  readonly totalCostUsd: number | null;
  readonly totalContainsEstimate: boolean;
}

interface InstallMockApiOptions {
  readonly seedCompletedProject?: boolean;
  readonly seedFailedProject?: boolean;
  readonly hostedProcessing?: HostedProcessingMock | null;
  readonly transcriptionOptions?: unknown;
  readonly capabilities?: unknown;
  readonly capabilitiesStatus?: number;
}

const NOW = '2026-04-02T12:00:00Z';
const OPENROUTER_DISCLOSURE = "Uses OpenRouter's hosted speech-to-text API. Requires an OpenRouter API key configured on the server. Audio is sent to the selected remote transcription provider.";
const MOBILE_VIEWPORTS = [
  { label: 'iphone-15', width: 393, height: 852 },
  { label: 'large-iphone', width: 430, height: 932 },
  { label: 'compact-android', width: 360, height: 800 },
];

const DIRECT_NATIVE_HOSTED_PROCESSING: HostedProcessingMock = {
  sttProvider: 'xAI',
  sttModel: 'grok-stt-1.0',
  audioDurationMs: 3_600_000,
  requestCount: 1,
  nativeDiarizationUsed: true,
  sttCostUsd: 0.1,
  sttRateUsdPerHour: 0.1,
  sttCostClassification: 'Estimated',
  diarizationSource: 'Provider',
  diarizationProvider: 'xAI',
  diarizationModel: 'grok-stt-1.0',
  diarizationRequestCount: 1,
  diarizationCostUsd: null,
  diarizationRateUsdPerHour: null,
  diarizationCostClassification: null,
  roleAttributionModel: 'google/gemini-3.7-flash',
  roleAttributionStatus: 'Completed',
  roleAttributionPromptTokens: 1200,
  roleAttributionOutputTokens: 80,
  roleAttributionCostUsd: 0.002,
  totalCostUsd: 0.102,
  totalContainsEstimate: true,
};

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify(body),
  });
}

function createMockState(seedCompletedProject = false, seedFailedProject = false): MockState {
  const state: MockState = {
    folders: [],
    projects: [],
    settings: {
      defaultEngine: 'WhisperNet',
      defaultModel: 'small',
      defaultLanguageMode: 'Auto',
      defaultLanguageCode: null,
      defaultAudioNormalizationEnabled: true,
      defaultDiarizationEnabled: false,
      defaultDiarizationSource: 'Local',
      defaultDiarizationMode: 'Basic',
      defaultSpeakerRoleAttributionEnabled: false,
      defaultTranscriptViewMode: 'Timestamped',
    },
  };

  if (seedCompletedProject || seedFailedProject) {
    const folder: FolderState = {
      id: 'folder-1',
      name: 'Biology',
      iconKey: 'Science',
      colorHex: '#2E7D32',
      createdAtUtc: NOW,
      updatedAtUtc: NOW,
    };
    const project: ProjectState = {
      id: 'project-1',
      folderId: folder.id,
      name: 'Biology Lecture 01',
      originalFileName: 'lecture01.mp3',
      status: 'Queued',
      progress: 100,
      mediaType: 'Audio',
      durationMs: 3_600_000,
      transcriptionElapsedMs: 522_000,
      totalSizeBytes: 412_345_678,
      createdAtUtc: NOW,
      updatedAtUtc: NOW,
      settings: {
        engine: state.settings.defaultEngine,
        model: state.settings.defaultModel,
        languageMode: state.settings.defaultLanguageMode,
        languageCode: state.settings.defaultLanguageCode,
        audioNormalizationEnabled: state.settings.defaultAudioNormalizationEnabled,
        diarizationEnabled: state.settings.defaultDiarizationEnabled,
        diarizationSource: state.settings.defaultDiarizationSource,
        diarizationMode: state.settings.defaultDiarizationMode,
        speakerRoleAttributionEnabled: state.settings.defaultSpeakerRoleAttributionEnabled,
      },
      transcriptAvailable: true,
      availableExports: ['txt', 'md', 'html', 'pdf'],
      originalFileSizeBytes: 385_000_000,
      workspaceSizeBytes: 27_345_678,
      detailRequests: 2,
    };

    if (seedFailedProject) {
      project.status = 'Failed';
      project.progress = null;
      project.transcriptAvailable = false;
      project.availableExports = [];
    } else {
      completeProject(project);
    }
    state.folders.push(folder);
    state.projects.push(project);
  }

  return state;
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
    debugTimings: project.status === 'Completed' ? {
      totalElapsedMs: 528_250,
      preparationElapsedMs: 6_250,
      inspectElapsedMs: 300,
      extractElapsedMs: 4_100,
      normalizeElapsedMs: 1_850,
      transcriptionElapsedMs: 510_000,
      persistElapsedMs: 5_750,
      transcriptionRealtimeFactor: 0.14,
      totalRealtimeFactor: 0.15,
    } : null,
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

async function installMockApi(page: Page, options?: InstallMockApiOptions) {
  const state = createMockState(options?.seedCompletedProject, options?.seedFailedProject);
  const hostedProcessing = options?.hostedProcessing === undefined
    ? DIRECT_NATIVE_HOSTED_PROCESSING
    : options.hostedProcessing;
  const transcriptionOptions = options?.transcriptionOptions ?? {
    engines: [
      { engine: 'WhisperNet', models: ['tiny', 'base', 'small'], providerDiarizationModels: [], wordTimestampModels: [] },
      { engine: 'WhisperNetCuda', models: ['small'], providerDiarizationModels: [], wordTimestampModels: [] },
      { engine: 'OpenVinoGenAi', models: ['base-int8', 'small-fp16', 'tiny-int8'], providerDiarizationModels: [], wordTimestampModels: [] },
      { engine: 'OpenRouter', models: ['openai/whisper-large-v3', 'deepgram/nova-3'], providerDiarizationModels: [], wordTimestampModels: ['openai/whisper-large-v3'] },
      { engine: 'Xai', models: ['grok-stt-1.0'], providerDiarizationModels: ['grok-stt-1.0'], wordTimestampModels: ['grok-stt-1.0'] },
    ],
    speakerRoleAttributionAvailable: true,
    speakerRoleAttributionModel: 'google/gemini-3.7-flash',
    recommendedHostedEngine: 'Xai',
    recommendedHostedModel: 'grok-stt-1.0',
    xaiDiarizationAvailable: true,
    xaiDiarizationModel: 'grok-stt-1.0',
  };

  await page.route('**/api/**', async (route) => {
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

    if (pathname === '/api/settings/options' && method === 'GET') {
      return json(route, transcriptionOptions);
    }

    if (pathname === '/api/settings/capabilities' && method === 'GET') {
      return json(route, options?.capabilities ?? {
        collectedAtUtc: NOW,
        hostedProviders: [
          { provider: 'OpenRouter', configured: true, reachable: true, status: 'Reachable.' },
          { provider: 'xAI', configured: true, reachable: false, status: 'Configured but unreachable.' },
        ],
        computeBackends: [
          { backend: 'CPU', available: true, devices: ['CPU device'], status: 'Available.' },
          { backend: 'CUDA', available: false, devices: [], status: 'Unavailable.' },
          { backend: 'CoreML', available: false, devices: [], status: 'Unavailable.' },
          { backend: 'OpenVINO', available: true, devices: ['OpenVINO device'], status: 'Available.' },
        ],
        architecture: 'X64',
        logicalProcessorCount: 16,
        osDescription: 'Test OS',
        hardwareName: 'Test CPU',
      }, options?.capabilitiesStatus);
    }

    if (pathname === '/api/settings/models' && method === 'GET') {
      return json(route, {
        models: [
          {
            engine: 'WhisperNet',
            model: 'small',
            isInstalled: true,
            installPath: '/models/whispernet/small.bin',
            canDownload: true,
            canRedownload: true,
            canProbe: true,
            probeState: 'Ready',
            probeMessage: 'Model is ready.',
          },
          {
            engine: 'WhisperNetCuda',
            model: 'small',
            isInstalled: false,
            installPath: null,
            canDownload: true,
            canRedownload: false,
            canProbe: false,
            probeState: 'Missing',
            probeMessage: 'Install the runtime to enable probes.',
          },
        ],
      });
    }

    if (pathname === '/api/settings/models/manage' && method === 'POST') {
      return json(route, { ok: true });
    }

    if (pathname === '/api/diagnostics' && method === 'GET') {
      return json(route, {
        runtime: {
          collectedAtUtc: NOW,
          processId: 4242,
          processorCount: 16,
          uptimeMs: 3_600_000,
          cpuUsagePercent: 12.4,
          workingSetBytes: 512_000_000,
          privateMemoryBytes: 389_000_000,
          managedHeapBytes: 112_000_000,
        },
        engines: [
          {
            engine: 'WhisperNet',
            isAvailable: true,
            models: ['small'],
            availabilityError: null,
          },
          {
            engine: 'WhisperNetCuda',
            isAvailable: false,
            models: ['small'],
            availabilityError: 'CUDA runtime libraries were not detected.',
          },
        ],
        projects: state.projects.map((project) => ({
          projectId: project.id,
          folderId: project.folderId,
          folderName: state.folders.find((folder) => folder.id === project.folderId)?.name ?? 'Unknown',
          projectName: project.name,
          status: project.status,
          originalFileSizeBytes: project.originalFileSizeBytes,
          workspaceSizeBytes: project.workspaceSizeBytes,
          totalSizeBytes: project.totalSizeBytes,
          updatedAtUtc: project.updatedAtUtc,
        })),
      });
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
          diarizationSource: state.settings.defaultDiarizationSource,
          diarizationMode: state.settings.defaultDiarizationMode,
          speakerRoleAttributionEnabled: state.settings.defaultSpeakerRoleAttributionEnabled,
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
        hostedProcessing,
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

async function assertNoHorizontalOverflow(page: Page) {
  const dimensions = await page.evaluate(() => ({
    root: document.documentElement.scrollWidth,
    body: document.body.scrollWidth,
    viewport: window.innerWidth,
  }));

  expect(dimensions.root).toBeLessThanOrEqual(dimensions.viewport + 1);
  expect(dimensions.body).toBeLessThanOrEqual(dimensions.viewport + 1);
}

async function expectTabToBeFullyVisible(tab: Locator) {
  const isFullyVisible = await tab.evaluate((element) => {
    const tabList = element.closest('[role="tablist"]');
    const scroller = tabList?.parentElement;
    if (!scroller) return false;

    const tabBounds = element.getBoundingClientRect();
    const scrollerBounds = scroller.getBoundingClientRect();
    return tabBounds.left >= scrollerBounds.left && tabBounds.right <= scrollerBounds.right;
  });

  expect(isFullyVisible).toBe(true);
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

  await expect(page.getByRole('heading', { name: 'Review Batch Upload' })).toBeVisible();
  await expect(page.getByRole('combobox').nth(0)).toHaveText('WhisperNet.CPU');
  await expect(page.getByRole('combobox').nth(1)).toHaveText('small');
  await page.getByRole('button', { name: 'Create and Queue' }).click();

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

test('selects OpenRouter defaults without treating remote models as local installs', async ({ page }) => {
  await installMockApi(page);

  await page.goto('/settings');
  await page.getByRole('combobox').nth(0).click();
  await page.getByRole('option', { name: 'OpenRouter' }).click();

  await expect(page.getByRole('combobox').nth(1)).toHaveText(/openai\/whisper-large-v3/);
  await expect(page.getByText(OPENROUTER_DISCLOSURE, { exact: true })).toBeVisible();
  await expect(page.getByRole('table').getByText('OpenRouter', { exact: true })).toHaveCount(0);

  const diarizationSwitch = page.getByRole('switch').nth(1);
  await diarizationSwitch.click();
  await expect(page.getByRole('combobox', { name: 'Diarization Source' })).toHaveText(/Local mode/);
  await expect(page.getByRole('option', { name: 'Provider mode' })).toHaveCount(0);
  await expect(page.getByRole('combobox', { name: 'Local Diarization Mode' })).toBeVisible();

  await page.setViewportSize({ width: 375, height: 812 });
  await page.getByRole('tab', { name: 'Local Model Manager', exact: true }).click();
  await expect(page.getByText(/swipe horizontally to view model status and actions/i)).toBeVisible();
  const modelManagerScroll = page.getByTestId('model-manager-scroll');
  await expect.poll(() => modelManagerScroll.evaluate((element) => element.scrollWidth > element.clientWidth)).toBe(true);
  await modelManagerScroll.evaluate((element) => {
    element.scrollLeft = element.scrollWidth;
  });
  const probeIsHorizontallyReachable = await page.getByRole('button', { name: 'Probe' }).first().evaluate((button) => {
    const scrollContainer = button.closest('[data-testid="model-manager-scroll"]');
    if (!scrollContainer) return false;
    const buttonRect = button.getBoundingClientRect();
    const containerRect = scrollContainer.getBoundingClientRect();
    return buttonRect.left >= containerRect.left && buttonRect.right <= containerRect.right;
  });
  expect(probeIsHorizontallyReachable).toBe(true);
});

test('shows direct xAI disclosure, role attribution, and hosted processing costs', async ({ page }) => {
  await installMockApi(page, { seedCompletedProject: true });

  await page.goto('/settings');
  await page.getByRole('combobox').nth(0).click();
  await page.getByRole('option', { name: 'xAI (direct)' }).click();
  await expect(page.getByText(/entire prepared audio file is sent directly to xAI/i)).toBeVisible();
  const roleSwitch = page.getByText('Speaker Role Attribution').locator('..').locator('..').getByRole('switch');
  await expect(roleSwitch).toBeDisabled();
  const diarizationSwitch = page.getByRole('switch').nth(1);
  await diarizationSwitch.click();
  const sourceSelect = page.getByRole('combobox', { name: 'Diarization Source' });
  await expect(sourceSelect).toHaveText(/Provider mode/);
  await expect(page.getByRole('combobox', { name: 'Local Diarization Mode' })).toHaveCount(0);
  await sourceSelect.click();
  await page.getByRole('option', { name: 'Local mode' }).click();
  await expect(page.getByRole('combobox', { name: 'Local Diarization Mode' })).toHaveText(/Basic/);

  await page.goto('/projects/project-1');
  await expect(page.getByText('Processing Details')).toBeVisible();
  await page.getByText('Processing Details').click();
  await expect(page.getByText('STT model / engine')).toBeVisible();
  await expect(page.getByText('Local processing')).toBeVisible();
  await expect(page.getByText('Speaker-role attribution')).toBeVisible();
  await expect(page.getByText(/Total \(includes estimate\): \$0\.1020/)).toBeVisible();
  await expect(page.getByText(/Role cost: \$0\.0020 \(Actual\)/)).toBeVisible();
});

test('hybrid processing details show actual and estimated costs', async ({ page }, testInfo) => {
  await installMockApi(page, {
    seedCompletedProject: true,
    capabilities: {
      collectedAtUtc: NOW,
      hostedProviders: [
        { provider: 'OpenRouter', configured: true, reachable: true, status: 'Reachable.' },
        { provider: 'xAI', configured: true, reachable: false, status: 'Configured but unreachable.' },
      ],
      computeBackends: [
        { backend: 'CPU', available: true, devices: ['CPU device'], status: 'Available.' },
        { backend: 'OpenVINO', available: true, devices: ['OpenVINO device'], status: 'Available.' },
      ],
      architecture: 'X64',
      logicalProcessorCount: 16,
      osDescription: 'Test OS',
      hardwareName: 'Test CPU',
      apiKey: 'SENSITIVE_CAPABILITY_SENTINEL',
      baseUrl: 'https://private.example.test/SENSITIVE_CAPABILITY_SENTINEL',
      deviceId: 'SENSITIVE_CAPABILITY_SENTINEL',
    },
    hostedProcessing: {
      sttProvider: 'OpenRouter',
      sttModel: 'openai/whisper-large-v3',
      audioDurationMs: 3_600_000,
      requestCount: 3,
      nativeDiarizationUsed: false,
      sttCostUsd: 0.07,
      sttRateUsdPerHour: 0.07,
      sttCostClassification: 'Actual',
      diarizationSource: 'Xai',
      diarizationProvider: 'xAI',
      diarizationModel: 'grok-stt-1.0',
      diarizationRequestCount: 1,
      diarizationCostUsd: 0.1,
      diarizationRateUsdPerHour: 0.1,
      diarizationCostClassification: 'Estimated',
      roleAttributionModel: 'google/gemini-3.7-flash',
      roleAttributionStatus: 'Completed',
      roleAttributionPromptTokens: 1200,
      roleAttributionOutputTokens: 80,
      roleAttributionCostUsd: 0.002,
      totalCostUsd: 0.172,
      totalContainsEstimate: true,
    },
  });

  for (const width of [375, 768, 1280]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/projects/project-1');
    const details = page.getByRole('button', { name: /Processing Details/ });
    await expect(details).toHaveAttribute('aria-controls', 'processing-details-panel');
    await details.focus();
    await details.press('Enter');
    await expect(details).toHaveAttribute('aria-expanded', 'true');
    await expect(page.getByRole('heading', { name: 'Speaker diarization' })).toBeVisible();
    await expect(page.getByText('Diarization source: xAI')).toBeVisible();
    await expect(page.getByText(/STT cost: \$0\.0700 \(Actual\)/)).toBeVisible();
    await expect(page.getByText(/Diarization cost: \$0\.1000 \(Estimated\)/)).toBeVisible();
    await expect(page.getByText(/Role cost: \$0\.0020 \(Actual\)/)).toBeVisible();
    await expect(page.getByText(/Total \(includes estimate\): \$0\.1720/)).toBeVisible();
    await assertNoHorizontalOverflow(page);
    await page.screenshot({
      path: testInfo.outputPath(`hybrid-processing-${width}.png`),
      fullPage: true,
      animations: 'disabled',
    });
  }
});

test('direct xAI and legacy processing metadata avoid duplicate or ghost costs', async ({ page }, testInfo) => {
  await installMockApi(page, { seedCompletedProject: true });
  await page.goto('/projects/project-1');
  await page.getByRole('button', { name: /Processing Details/ }).click();
  await expect(page.getByText('Diarization cost: Included in STT (no separate diarization charge)')).toBeVisible();
  await expect(page.getByText(/Diarization cost: \$0\./)).toHaveCount(0);
  await page.screenshot({
    path: testInfo.outputPath('direct-native-processing.png'),
    fullPage: true,
    animations: 'disabled',
  });

  await page.unroute('**/api/**');
  await installMockApi(page, { seedCompletedProject: true, hostedProcessing: null });
  await page.goto('/projects/project-1');
  await page.getByRole('button', { name: /Processing Details/ }).click();
  await expect(page.getByRole('heading', { name: 'Speaker diarization' })).toBeVisible();
  await expect(page.getByText('Diarization source: Not used')).toBeVisible();
  await expect(page.getByText(/STT cost:|Diarization cost:|Total \(/)).toHaveCount(0);
  await expect(page.getByText(/undefined|NaN/)).toHaveCount(0);
  await assertNoHorizontalOverflow(page);
  await page.screenshot({
    path: testInfo.outputPath('legacy-processing.png'),
    fullPage: true,
    animations: 'disabled',
  });
});

test('keeps the selected Settings tab fully visible after 375px keyboard traversal', async ({ page }) => {
  // Given a narrow Settings tab strip.
  await installMockApi(page);
  await page.setViewportSize({ width: 375, height: 812 });
  await page.goto('/settings');

  const settingsTab = page.getByRole('tab', { name: 'Settings', exact: true });
  const modelManagerTab = page.getByRole('tab', { name: 'Local Model Manager', exact: true });
  const capabilitiesTab = page.getByRole('tab', { name: 'System Capabilities', exact: true });

  // When the native roving-tab interaction traverses away from and back to Settings.
  await settingsTab.focus();
  await settingsTab.press('ArrowRight');
  await modelManagerTab.press('ArrowRight');
  await capabilitiesTab.press('ArrowLeft');
  await modelManagerTab.press('ArrowLeft');

  // Then the selected tab is not clipped or scrolled outside the tab strip.
  await expect(settingsTab).toHaveAttribute('aria-selected', 'true');
  await expectTabToBeFullyVisible(settingsTab);
});

test('keeps a pointer-selected Settings tab fully visible at 375px', async ({ page }) => {
  // Given a narrow Settings tab strip positioned at its far end.
  await installMockApi(page);
  await page.setViewportSize({ width: 375, height: 812 });
  await page.goto('/settings');

  const settingsTab = page.getByRole('tab', { name: 'Settings', exact: true });
  const modelManagerTab = page.getByRole('tab', { name: 'Local Model Manager', exact: true });
  const capabilitiesTab = page.getByRole('tab', { name: 'System Capabilities', exact: true });
  await settingsTab.focus();
  await settingsTab.press('End');
  await expectTabToBeFullyVisible(capabilitiesTab);

  // When a visible Settings tab is selected with a pointer click.
  await modelManagerTab.click();

  // Then its full label remains inside the scrollable tab strip.
  await expect(modelManagerTab).toHaveAttribute('aria-selected', 'true');
  await expectTabToBeFullyVisible(modelManagerTab);
});

test('keeps the desktop shell anchored across keyboard and pointer tab changes', async ({ page }) => {
  // Given the full desktop Settings shell at its initial document position.
  await installMockApi(page);
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto('/settings');

  const settingsTab = page.getByRole('tab', { name: 'Settings', exact: true });
  const modelManagerTab = page.getByRole('tab', { name: 'Local Model Manager', exact: true });
  const capabilitiesTab = page.getByRole('tab', { name: 'System Capabilities', exact: true });

  // When native keyboard traversal and a pointer selection activate the non-default tabs.
  await settingsTab.focus();
  await settingsTab.press('ArrowRight');
  await modelManagerTab.press('ArrowRight');
  await modelManagerTab.click();

  // Then no outer document scrolling or wide-shell displacement occurs.
  await expect.poll(() => page.evaluate(() => ({ x: window.scrollX, y: window.scrollY }))).toEqual({ x: 0, y: 0 });
  await expect(page.getByText('TranscriptLab', { exact: true })).toBeVisible();
  await expectTabToBeFullyVisible(settingsTab);
  await expectTabToBeFullyVisible(modelManagerTab);
  await expectTabToBeFullyVisible(capabilitiesTab);
});

test('settings tabs lazy loading and keyboard', async ({ page }, testInfo) => {
  const modelRequests: string[] = [];
  const capabilityRequests: string[] = [];
  page.on('request', (request) => {
    const pathname = new URL(request.url()).pathname;
    if (pathname === '/api/settings/models') modelRequests.push(pathname);
    if (pathname === '/api/settings/capabilities') capabilityRequests.push(pathname);
  });
  await installMockApi(page);

  for (const width of [375, 768, 1280]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/settings');
    const modelRequestCountBeforeActivation = modelRequests.length;
    const capabilityRequestCountBeforeActivation = capabilityRequests.length;

    const settingsTab = page.getByRole('tab', { name: 'Settings', exact: true });
    const modelManagerTab = page.getByRole('tab', { name: 'Local Model Manager', exact: true });
    const capabilitiesTab = page.getByRole('tab', { name: 'System Capabilities', exact: true });
    await expect(settingsTab).toHaveAttribute('aria-selected', 'true');
    await expect(modelManagerTab).toHaveAttribute('aria-selected', 'false');
    await expect(capabilitiesTab).toHaveAttribute('aria-selected', 'false');
    await expect(settingsTab).toHaveAttribute('aria-controls', 'settings-tabpanel');
    await expect(page.getByRole('tabpanel', { name: 'Settings' })).toHaveAttribute('aria-labelledby', 'settings-tab');
    await expect(page.locator('header').getByRole('button', { name: 'Save' })).toBeVisible();
    await expect(page.locator('header').getByRole('button', { name: 'Reset' })).toBeVisible();
    expect(modelRequests).toHaveLength(modelRequestCountBeforeActivation);
    expect(capabilityRequests).toHaveLength(capabilityRequestCountBeforeActivation);

    await page.getByRole('combobox', { name: 'Engine' }).click();
    await page.getByRole('option', { name: 'OpenRouter' }).click();
    const documentScrollBeforeTabTraversal = await page.evaluate(() => ({ x: window.scrollX, y: window.scrollY }));
    expect(documentScrollBeforeTabTraversal).toEqual({ x: 0, y: 0 });

    await settingsTab.focus();
    await settingsTab.press('ArrowRight');
    await expect(modelManagerTab).toHaveAttribute('aria-selected', 'true');
    await expect(page.getByRole('tabpanel', { name: 'Local Model Manager' })).toBeVisible();
    await expect(page.locator('header').getByRole('button', { name: 'Save' })).toHaveCount(0);
    await expect(page.locator('header').getByRole('button', { name: 'Reset' })).toHaveCount(0);
    await expect.poll(() => modelRequests.length).toBe(modelRequestCountBeforeActivation + 1);
    expect(capabilityRequests).toHaveLength(capabilityRequestCountBeforeActivation);
    await expectTabToBeFullyVisible(modelManagerTab);
    await assertNoHorizontalOverflow(page);
    await page.screenshot({
      path: testInfo.outputPath(`local-model-manager-${width}.png`),
      fullPage: true,
      animations: 'disabled',
    });

    await modelManagerTab.press('ArrowRight');
    await expect(capabilitiesTab).toHaveAttribute('aria-selected', 'true');
    const capabilitiesPanel = page.getByRole('tabpanel', { name: 'System Capabilities' });
    await expect(capabilitiesPanel).toBeVisible();
    await expect.poll(() => capabilityRequests.length).toBe(capabilityRequestCountBeforeActivation + 1);
    await expect(capabilitiesPanel.getByRole('heading', { name: 'Hosted providers' })).toBeVisible();
    await expect(capabilitiesPanel.getByText('OpenRouter', { exact: true })).toBeVisible();
    await expect(capabilitiesPanel.getByText('OpenVINO', { exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Refresh capabilities' }).click();
    await expect.poll(() => capabilityRequests.length).toBe(capabilityRequestCountBeforeActivation + 2);
    await expect.poll(() => page.evaluate(() => window.scrollX)).toBe(documentScrollBeforeTabTraversal.x);
    if (width === 1280) {
      await expect.poll(() => page.evaluate(() => window.scrollY)).toBe(documentScrollBeforeTabTraversal.y);
      await expect(page.getByText('TranscriptLab', { exact: true })).toBeVisible();
      await expectTabToBeFullyVisible(settingsTab);
    }
    await assertNoHorizontalOverflow(page);
    await page.screenshot({
      path: testInfo.outputPath(`system-capabilities-${width}.png`),
      fullPage: true,
      animations: 'disabled',
    });

    await capabilitiesTab.press('ArrowLeft');
    await expect(modelManagerTab).toHaveAttribute('aria-selected', 'true');
    await modelManagerTab.press('ArrowLeft');
    await expect(settingsTab).toHaveAttribute('aria-selected', 'true');
    await expect(page.getByRole('combobox', { name: 'Engine' })).toHaveText('OpenRouter');
    await assertNoHorizontalOverflow(page);
    await page.screenshot({
      path: testInfo.outputPath(`settings-tabs-${width}.png`),
      fullPage: true,
      animations: 'disabled',
    });
  }
});

test('verified OpenRouter exposes xAI source in settings, upload, and retry', async ({ page }) => {
  await installMockApi(page, { seedFailedProject: true });

  await page.goto('/settings');
  await page.getByRole('combobox', { name: 'Engine' }).click();
  await page.getByRole('option', { name: 'OpenRouter' }).click();
  await page.getByRole('switch').nth(1).click();
  await page.getByRole('combobox', { name: 'Diarization Source' }).click();
  await expect(page.getByRole('option', { name: 'xAI mode' })).toBeVisible();
  await page.getByRole('option', { name: 'xAI mode' }).click();
  await expect(page.getByText(/whole prepared FLAC/i)).toBeVisible();
  await expect(page.getByText(/job fails/i)).toBeVisible();

  await page.goto('/folders/folder-1');
  await page.locator('input[type="file"]').setInputFiles({
    name: 'lecture02.mp3',
    mimeType: 'audio/mpeg',
    buffer: Buffer.from('mock-audio'),
  });
  await page.getByRole('combobox', { name: 'Engine' }).click();
  await page.getByRole('option', { name: 'OpenRouter' }).click();
  await page.getByRole('switch').nth(1).click();
  await page.getByRole('combobox', { name: 'Diarization Source' }).click();
  await expect(page.getByRole('option', { name: 'xAI mode' })).toBeVisible();
  await page.getByRole('option', { name: 'xAI mode' }).click();
  await expect(page.getByText(/whole prepared FLAC/i)).toBeVisible();
  await page.getByRole('button', { name: 'Cancel' }).click();

  await page.goto('/projects/project-1');
  await page.getByRole('button', { name: 'Retry' }).first().click();
  await expect(page.getByRole('dialog', { name: 'Retry Project' })).toBeVisible();
  await page.getByRole('dialog').getByRole('combobox', { name: 'Engine' }).click();
  await page.getByRole('option', { name: 'OpenRouter' }).click();
  await page.getByRole('dialog').getByRole('switch').nth(1).click();
  await page.getByRole('dialog').getByRole('combobox', { name: 'Diarization Source' }).click();
  await expect(page.getByRole('option', { name: 'xAI mode' })).toBeVisible();
  await page.getByRole('option', { name: 'xAI mode' }).click();
  await expect(page.getByRole('dialog').getByText(/whole prepared FLAC/i)).toBeVisible();
});

test('unsupported model hides xAI and sensitive capability fields', async ({ page }, testInfo) => {
  await installMockApi(page, {
    transcriptionOptions: {
      engines: [
        { engine: 'WhisperNet', models: ['small'], providerDiarizationModels: [], wordTimestampModels: [] },
        { engine: 'OpenRouter', models: ['deepgram/nova-3'], providerDiarizationModels: [], wordTimestampModels: ['deepgram/nova-3'] },
      ],
      speakerRoleAttributionAvailable: false,
      speakerRoleAttributionModel: '',
      recommendedHostedEngine: null,
      recommendedHostedModel: null,
      xaiDiarizationAvailable: true,
      xaiDiarizationModel: 'grok-stt-1.0',
    },
    capabilitiesStatus: 500,
    capabilities: {
      code: 'internal_error',
      message: 'SHOULD_NOT_RENDER_API_KEY_OR_PRIVATE_URL',
      apiKey: 'SHOULD_NOT_RENDER_API_KEY_OR_PRIVATE_URL',
      baseUrl: 'https://private.example.test/SHOULD_NOT_RENDER_API_KEY_OR_PRIVATE_URL',
      deviceId: 'GPU-SHOULD_NOT_RENDER_API_KEY_OR_PRIVATE_URL',
    },
  });

  await page.setViewportSize({ width: 375, height: 812 });
  await page.goto('/settings');
  await page.getByRole('combobox', { name: 'Engine' }).click();
  await page.getByRole('option', { name: 'OpenRouter' }).click();
  await page.getByRole('switch').nth(1).click();
  await page.getByRole('combobox', { name: 'Diarization Source' }).click();
  await expect(page.getByRole('option', { name: 'xAI mode' })).toHaveCount(0);
  await page.keyboard.press('Escape');

  await page.getByRole('tab', { name: 'System Capabilities' }).click();
  await expect(page.getByText('Unable to load system capabilities.')).toBeVisible();
  await expect(page.getByText('SHOULD_NOT_RENDER_API_KEY_OR_PRIVATE_URL')).toHaveCount(0);
  await assertNoHorizontalOverflow(page);
  await page.screenshot({
    path: testInfo.outputPath('unsupported-capabilities-375.png'),
    fullPage: true,
    animations: 'disabled',
  });
});

for (const viewport of MOBILE_VIEWPORTS) {
  test(`renders mobile layouts without horizontal overflow on ${viewport.label}`, async ({ page }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await installMockApi(page, { seedCompletedProject: true });

    await page.goto('/');
    await expect(page.locator('header').getByRole('button', { name: 'Open navigation' })).toBeVisible();
    await assertNoHorizontalOverflow(page);

    await page.locator('header').getByRole('button', { name: 'Open navigation' }).click();
    await page.getByRole('button', { name: 'Diagnostics' }).click();
    await expect(page).toHaveURL(/\/diagnostics$/);
    await assertNoHorizontalOverflow(page);

    await page.locator('header').getByRole('button', { name: 'Open navigation' }).click();
    await page.getByRole('button', { name: 'Settings' }).click();
    await expect(page).toHaveURL(/\/settings$/);
    await expect(page.getByRole('tab', { name: 'Local Model Manager', exact: true })).toBeVisible();
    await assertNoHorizontalOverflow(page);

    await page.goto('/folders/folder-1');
    await expect(page.getByText('Biology Lecture 01')).toBeVisible();
    await assertNoHorizontalOverflow(page);
    await page.locator('input[type="file"]').setInputFiles([
      {
        name: 'lecture02.mp3',
        mimeType: 'audio/mpeg',
        buffer: Buffer.from('mock-audio'),
      },
    ]);
    await expect(page.getByRole('button', { name: 'Create and Queue' })).toBeVisible();
    await page.getByRole('button', { name: 'Cancel' }).click();

    await page.goto('/queue');
    await expect(page.getByText('WhisperNet.CPU / small')).toBeVisible();
    await assertNoHorizontalOverflow(page);

    await page.goto('/projects/project-1');
    await expect(page.getByRole('button', { name: 'Readable' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Timestamped' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Play' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Export' })).toBeVisible();
    await assertNoHorizontalOverflow(page);
  });
}

test('captures unified processing details at responsive breakpoints', async ({ page }, testInfo) => {
  await installMockApi(page, { seedCompletedProject: true });
  for (const width of [375, 768, 1280]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/projects/project-1');
    await expect(page.getByText('Processing Details')).toBeVisible();
    await page.getByText('Processing Details').click();
    await expect(page.getByText('STT model / engine')).toBeVisible();
    await assertNoHorizontalOverflow(page);
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({
      path: testInfo.outputPath(`processing-details-${width}.png`),
      fullPage: true,
      animations: 'disabled',
    });
  }
});

test('captures diarization source modes at responsive breakpoints', async ({ page }, testInfo) => {
  await installMockApi(page);

  for (const width of [375, 768, 1280]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/settings');

    await page.getByRole('combobox', { name: 'Engine' }).click();
    await page.getByRole('option', { name: 'xAI (direct)' }).click();
    await expect(page.getByRole('listbox')).toHaveCount(0);
    await page.getByRole('switch').nth(1).click();
    await expect(page.getByRole('combobox', { name: 'Diarization Source' })).toHaveText(/Provider mode/);
    await expect(page.getByRole('combobox', { name: 'Local Diarization Mode' })).toHaveCount(0);
    await assertNoHorizontalOverflow(page);
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({
      path: testInfo.outputPath(`diarization-xai-provider-${width}.png`),
      fullPage: true,
      animations: 'disabled',
    });

    await page.getByRole('combobox', { name: 'Diarization Source' }).click();
    await page.getByRole('option', { name: 'Local mode' }).click();
    await expect(page.getByRole('listbox')).toHaveCount(0);
    await expect(page.getByRole('combobox', { name: 'Local Diarization Mode' })).toBeVisible();
    await assertNoHorizontalOverflow(page);
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({
      path: testInfo.outputPath(`diarization-xai-local-${width}.png`),
      fullPage: true,
      animations: 'disabled',
    });

    await page.getByRole('combobox', { name: 'Engine' }).click();
    await page.getByRole('option', { name: 'OpenRouter' }).click();
    await expect(page.getByRole('listbox')).toHaveCount(0);
    await expect(page.getByRole('combobox', { name: 'Diarization Source' })).toHaveText(/Local mode/);
    await expect(page.getByRole('combobox', { name: 'Local Diarization Mode' })).toBeVisible();
    await assertNoHorizontalOverflow(page);
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({
      path: testInfo.outputPath(`diarization-openrouter-local-${width}.png`),
      fullPage: true,
      animations: 'disabled',
    });
  }
});

test('integrated hosted long-form surfaces preserve settings, costs, capabilities, and diagnostics', async ({ page }, testInfo) => {
  await installMockApi(page, {
    seedCompletedProject: true,
    hostedProcessing: {
      sttProvider: 'OpenRouter',
      sttModel: 'openai/whisper-large-v3',
      audioDurationMs: 1_201_000,
      requestCount: 3,
      nativeDiarizationUsed: false,
      sttCostUsd: 0.07,
      sttRateUsdPerHour: 0.209825,
      sttCostClassification: 'Actual',
      diarizationSource: 'Xai',
      diarizationProvider: 'xAI',
      diarizationModel: 'grok-stt-1.0',
      diarizationRequestCount: 1,
      diarizationCostUsd: 0.033361,
      diarizationRateUsdPerHour: 0.1,
      diarizationCostClassification: 'Estimated',
      roleAttributionModel: null,
      roleAttributionStatus: null,
      roleAttributionPromptTokens: null,
      roleAttributionOutputTokens: null,
      roleAttributionCostUsd: null,
      totalCostUsd: 0.103361,
      totalContainsEstimate: true,
    },
  });

  for (const width of [375, 768, 1280]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/settings');
    await page.getByRole('tab', { name: 'System Capabilities' }).click();
    await expect(page.getByText('OpenRouter', { exact: true })).toBeVisible();
    await expect(page.getByText('xAI', { exact: true })).toBeVisible();
    await expect(page.getByText('SENSITIVE_CAPABILITY_SENTINEL')).toHaveCount(0);

    await page.goto('/projects/project-1');
    const details = page.getByRole('button', { name: /Processing Details/ });
    await details.click();
    await expect(page.getByText(/STT cost: \$0\.0700 \(Actual\)/)).toBeVisible();
    await expect(page.getByText(/Diarization cost: \$0\.0334 \(Estimated\)/)).toBeVisible();
    await expect(page.getByText(/Total \(includes estimate\): \$0\.1034/)).toBeVisible();
    await assertNoHorizontalOverflow(page);
    await page.screenshot({
      path: testInfo.outputPath(`integrated-hosted-long-form-${width}.png`),
      fullPage: true,
      animations: 'disabled',
    });

    await page.goto('/diagnostics');
    await expect(page.getByRole('heading', { name: 'Diagnostics' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Runtime', exact: true })).toBeVisible();
    await expect(page.getByText('WhisperNet.CPU • small', { exact: true })).toBeVisible();
    await assertNoHorizontalOverflow(page);
  }
});
