import { useState, useCallback, useEffect } from 'react';
import {
  Box, Stack, Typography, Button, Chip, LinearProgress, Paper, Skeleton, Accordion, AccordionSummary, AccordionDetails, ToggleButtonGroup, ToggleButton,
} from '@mui/material';
import ReplayIcon from '@mui/icons-material/Replay';
import CancelIcon from '@mui/icons-material/Cancel';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import EditIcon from '@mui/icons-material/Edit';
import MovieIcon from '@mui/icons-material/Movie';
import GraphicEqIcon from '@mui/icons-material/GraphicEq';
import { useRoute } from 'wouter';
import TopBar from '../components/shell/TopBar';
import MediaPlayer from '../components/projects/MediaPlayer';
import TranscriptViewer from '../components/projects/TranscriptViewer';
import ExportMenu from '../components/projects/ExportMenu';
import RenameProjectDialog from '../components/projects/RenameProjectDialog';
import RetryProjectDialog from '../components/projects/RetryProjectDialog';
import ProjectStatusChip from '../components/common/ProjectStatusChip';
import StorageUsageSummary from '../components/common/StorageUsageSummary';
import EmptyState from '../components/common/EmptyState';
import { useProject, useSettings, useTranscript } from '../hooks/useData';
import { projectsApi } from '../api';
import { useNotification } from '../components/notifications';
import { useSWRConfig } from 'swr';
import { formatDate, formatDuration } from '../utils/format';
import { formatEngineLabel } from '../utils/transcription';
import { getLanguageLabel } from '../utils/languages';

function formatDebugDuration(ms: number | null | undefined): string {
  if (ms == null) return '—';
  if (ms < 1000) return `${ms} ms`;
  return `${(ms / 1000).toFixed(2)} s`;
}

function formatRealtimeFactor(value: number | null | undefined): string {
  if (value == null) return '—';
  return `${value.toFixed(2)}x`;
}

export default function ProjectDetailPage() {
  const [, params] = useRoute('/projects/:projectId');
  const projectId = params?.projectId ?? '';
  const { data: project, isLoading } = useProject(projectId);
  const { data: settings } = useSettings();
  const transcriptEnabled = project?.status === 'Completed' && project.transcriptAvailable;
  const { data: transcript, isLoading: transcriptLoading } = useTranscript(projectId, !!transcriptEnabled);
  const { notify } = useNotification();
  const { mutate } = useSWRConfig();
  const [actionLoading, setActionLoading] = useState(false);
  const [renameOpen, setRenameOpen] = useState(false);
  const [retryOpen, setRetryOpen] = useState(false);
  const [previewMode, setPreviewMode] = useState<'source' | 'audio'>('source');

  useEffect(() => {
    setPreviewMode('source');
  }, [projectId]);

  useEffect(() => {
    if (project?.audioPreviewUrl == null && previewMode === 'audio')
      setPreviewMode('source');
  }, [project?.audioPreviewUrl, previewMode]);

  const handleQueue = useCallback(async () => {
    setActionLoading(true);
    try {
      await projectsApi.queue(projectId);
      await mutate(`projects/${projectId}`);
      await mutate('queue');
      notify('Project queued');
    } catch {
      notify('Failed to queue', 'error');
    } finally {
      setActionLoading(false);
    }
  }, [projectId, mutate, notify]);

  const handleCancel = useCallback(async () => {
    setActionLoading(true);
    try {
      await projectsApi.cancel(projectId);
      await mutate(`projects/${projectId}`);
      await mutate('queue');
      notify('Project cancelled');
    } catch {
      notify('Failed to cancel', 'error');
    } finally {
      setActionLoading(false);
    }
  }, [projectId, mutate, notify]);

  if (isLoading) {
    return (
      <>
        <TopBar title="Loading..." breadcrumbs={[{ label: 'Folders', href: '/folders' }]} />
        <Skeleton variant="rounded" height={400} />
      </>
    );
  }

  if (!project) {
    return (
      <>
        <TopBar title="Not Found" />
        <EmptyState title="Project not found" description="This project may have been deleted." />
      </>
    );
  }

  const isProcessing = project.status === 'PreparingMedia' || project.status === 'Transcribing';
  const canPreviewExtractedAudio = project.mediaType === 'Video' && !!project.audioPreviewUrl;
  const mediaPlayerSource = previewMode === 'audio' && project.audioPreviewUrl
    ? project.audioPreviewUrl
    : project.mediaUrl;
  const mediaPlayerType = previewMode === 'audio' && project.audioPreviewUrl
    ? 'Audio'
    : project.mediaType;

  return (
    <>
      <TopBar
        title={project.name}
        breadcrumbs={[
          { label: 'Folders', href: '/folders' },
          { label: project.folderName, href: `/folders/${project.folderId}` },
          { label: project.name },
        ]}
        actions={
          <>
            {project.status === 'Draft' && (
              <Button variant="contained" startIcon={<PlayArrowIcon />} onClick={handleQueue} disabled={actionLoading}>
                Queue
              </Button>
            )}
            {project.status === 'Failed' && (
              <Button variant="outlined" startIcon={<ReplayIcon />} onClick={() => setRetryOpen(true)} disabled={actionLoading}>
                Retry
              </Button>
            )}
            {project.status === 'Queued' && (
              <Button variant="outlined" startIcon={<CancelIcon />} onClick={handleCancel} disabled={actionLoading}>
                Cancel
              </Button>
            )}
            {(project.status === 'PreparingMedia' || project.status === 'Transcribing') && (
              <Button variant="outlined" color="warning" startIcon={<CancelIcon />} onClick={handleCancel} disabled={actionLoading}>
                Stop
              </Button>
            )}
            <Button variant="outlined" startIcon={<EditIcon />} onClick={() => setRenameOpen(true)} disabled={actionLoading}>
              Edit Name
            </Button>
            {transcriptEnabled && (
              <ExportMenu projectId={projectId} availableExports={project.availableExports} />
            )}
          </>
        }
      />

      {/* Metadata bar */}
      <Paper variant="outlined" sx={{ p: 2, mb: 2 }}>
        <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 2, alignItems: 'center' }}>
          <ProjectStatusChip status={project.status} size="medium" />
          {project.durationMs != null && (
            <Chip label={`Duration: ${formatDuration(project.durationMs)}`} size="small" variant="outlined" />
          )}
          {project.transcriptionElapsedMs != null && (
            <Chip label={`Transcribed in: ${formatDuration(project.transcriptionElapsedMs)}`} size="small" variant="outlined" />
          )}
          <Chip label={project.mediaType} size="small" variant="outlined" />
          <Chip label={`${formatEngineLabel(project.settings.engine)} / ${project.settings.model}`} size="small" variant="outlined" />
          {project.settings.languageMode === 'Fixed' && project.settings.languageCode && (
            <Chip label={`Lang: ${getLanguageLabel(project.settings.languageCode)}`} size="small" variant="outlined" />
          )}
          <Box sx={{ flexGrow: 1 }} />
          <StorageUsageSummary
            originalFileSizeBytes={project.originalFileSizeBytes}
            workspaceSizeBytes={project.workspaceSizeBytes}
            totalSizeBytes={project.totalSizeBytes}
            compact
          />
        </Box>
        {project.errorMessage && (
          <Typography variant="body2" color="error" sx={{ mt: 1 }}>
            Error: {project.errorMessage}
          </Typography>
        )}
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
          Created {formatDate(project.createdAtUtc)}
          {project.completedAtUtc && ` • Completed ${formatDate(project.completedAtUtc)}`}
        </Typography>
      </Paper>

      {project.debugTimings && (
        <Accordion variant="outlined" disableGutters sx={{ mb: 2 }}>
          <AccordionSummary expandIcon={<ExpandMoreIcon />}>
            <Typography variant="overline" color="text.secondary">
              Debug Timing
            </Typography>
          </AccordionSummary>
          <AccordionDetails sx={{ pt: 0 }}>
            <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
              <Chip size="small" variant="outlined" label={`Total: ${formatDebugDuration(project.debugTimings.totalElapsedMs)}`} />
              <Chip size="small" variant="outlined" label={`Prep: ${formatDebugDuration(project.debugTimings.preparationElapsedMs)}`} />
              <Chip size="small" variant="outlined" label={`Inspect: ${formatDebugDuration(project.debugTimings.inspectElapsedMs)}`} />
              <Chip size="small" variant="outlined" label={`Extract: ${formatDebugDuration(project.debugTimings.extractElapsedMs)}`} />
              <Chip size="small" variant="outlined" label={`Normalize: ${formatDebugDuration(project.debugTimings.normalizeElapsedMs)}`} />
              <Chip size="small" variant="outlined" label={`Transcribe: ${formatDebugDuration(project.debugTimings.transcriptionElapsedMs)}`} />
              <Chip size="small" variant="outlined" label={`Persist: ${formatDebugDuration(project.debugTimings.persistElapsedMs)}`} />
              <Chip size="small" variant="outlined" label={`Transcribe RT: ${formatRealtimeFactor(project.debugTimings.transcriptionRealtimeFactor)}`} />
              <Chip size="small" variant="outlined" label={`Total RT: ${formatRealtimeFactor(project.debugTimings.totalRealtimeFactor)}`} />
            </Box>
          </AccordionDetails>
        </Accordion>
      )}

      {/* Main workspace: stacked media + transcript */}
      <Stack spacing={2}>
        <Box>
          {canPreviewExtractedAudio && (
            <Box sx={{ display: 'flex', justifyContent: 'flex-end', mb: 1 }}>
              <ToggleButtonGroup
                exclusive
                size="small"
                value={previewMode}
                onChange={(_, value: 'source' | 'audio' | null) => {
                  if (value) setPreviewMode(value);
                }}
              >
                <ToggleButton value="source">
                  <MovieIcon sx={{ mr: 0.75, fontSize: 18 }} />
                  Video
                </ToggleButton>
                <ToggleButton value="audio">
                  <GraphicEqIcon sx={{ mr: 0.75, fontSize: 18 }} />
                  Extracted Audio
                </ToggleButton>
              </ToggleButtonGroup>
            </Box>
          )}
          <MediaPlayer
            src={mediaPlayerSource}
            mediaType={mediaPlayerType}
          />
        </Box>
        <Box>
          <Paper variant="outlined" sx={{ p: 2, height: '100%' }}>
            {project.status === 'Completed' && transcript ? (
              <TranscriptViewer
                transcript={transcript}
                defaultViewMode={settings?.defaultTranscriptViewMode}
              />
            ) : project.status === 'Completed' && transcriptEnabled && transcriptLoading ? (
              <EmptyState title="Loading transcript" description="The transcript is being loaded.">
                <LinearProgress sx={{ mt: 2, maxWidth: 300, mx: 'auto' }} />
              </EmptyState>
            ) : project.status === 'Failed' ? (
              <EmptyState
                title="Transcription Failed"
                description={project.errorMessage || 'An error occurred during processing.'}
                action={
                  <Button variant="contained" startIcon={<ReplayIcon />} onClick={() => setRetryOpen(true)} disabled={actionLoading}>
                    Retry
                  </Button>
                }
              />
            ) : isProcessing ? (
              <EmptyState title="Processing...">
                <LinearProgress sx={{ mt: 2, maxWidth: 300, mx: 'auto' }} />
              </EmptyState>
            ) : project.status === 'Queued' ? (
              <EmptyState title="Waiting in queue" description="This project is queued for processing." />
            ) : project.status === 'Draft' ? (
              <EmptyState
                title="Draft"
                description="This project is ready to be queued for transcription."
                action={
                  <Button variant="contained" startIcon={<PlayArrowIcon />} onClick={handleQueue} disabled={actionLoading}>
                    Queue for Transcription
                  </Button>
                }
              />
            ) : (
              <EmptyState title="No transcript" description="Upload and queue the project to generate a transcript." />
            )}
          </Paper>
        </Box>
      </Stack>

      <RenameProjectDialog
        open={renameOpen}
        projectId={project.id}
        currentName={project.name}
        folderId={project.folderId}
        onClose={() => setRenameOpen(false)}
      />

      <RetryProjectDialog
        open={retryOpen}
        projectId={project.id}
        project={project}
        onClose={() => setRetryOpen(false)}
      />
    </>
  );
}
