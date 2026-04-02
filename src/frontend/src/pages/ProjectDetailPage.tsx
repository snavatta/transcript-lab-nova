import { useState, useCallback } from 'react';
import {
  Box, Stack, Typography, Button, Chip, LinearProgress, Paper, Skeleton,
} from '@mui/material';
import ReplayIcon from '@mui/icons-material/Replay';
import CancelIcon from '@mui/icons-material/Cancel';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import { useRoute } from 'wouter';
import TopBar from '../components/shell/TopBar';
import MediaPlayer from '../components/projects/MediaPlayer';
import TranscriptViewer from '../components/projects/TranscriptViewer';
import ExportMenu from '../components/projects/ExportMenu';
import ProjectStatusChip from '../components/common/ProjectStatusChip';
import StorageUsageSummary from '../components/common/StorageUsageSummary';
import EmptyState from '../components/common/EmptyState';
import { useProject, useSettings, useTranscript } from '../hooks/useData';
import { projectsApi } from '../api';
import { useNotification } from '../components/notifications';
import { useSWRConfig } from 'swr';
import { formatDate, formatDuration } from '../utils/format';

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

  const handleRetry = useCallback(async () => {
    setActionLoading(true);
    try {
      await projectsApi.retry(projectId);
      await mutate(`projects/${projectId}`);
      await mutate('queue');
      notify('Project re-queued');
    } catch {
      notify('Failed to retry', 'error');
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
              <Button variant="outlined" startIcon={<ReplayIcon />} onClick={handleRetry} disabled={actionLoading}>
                Retry
              </Button>
            )}
            {project.status === 'Queued' && (
              <Button variant="outlined" startIcon={<CancelIcon />} onClick={handleCancel} disabled={actionLoading}>
                Cancel
              </Button>
            )}
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
          {isProcessing && project.progress != null && (
            <Box sx={{ minWidth: 120 }}>
              <LinearProgress variant="determinate" value={project.progress} />
              <Typography variant="caption">{project.progress}%</Typography>
            </Box>
          )}
          {project.durationMs != null && (
            <Chip label={`Duration: ${formatDuration(project.durationMs)}`} size="small" variant="outlined" />
          )}
          <Chip label={project.mediaType} size="small" variant="outlined" />
          <Chip label={`${project.settings.engine} / ${project.settings.model}`} size="small" variant="outlined" />
          {project.settings.languageMode === 'Fixed' && project.settings.languageCode && (
            <Chip label={`Lang: ${project.settings.languageCode}`} size="small" variant="outlined" />
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

      {/* Main workspace: stacked media + transcript */}
      <Stack spacing={2}>
        <Box>
          <MediaPlayer
            src={project.mediaUrl}
            mediaType={project.mediaType}
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
                  <Button variant="contained" startIcon={<ReplayIcon />} onClick={handleRetry} disabled={actionLoading}>
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
    </>
  );
}
