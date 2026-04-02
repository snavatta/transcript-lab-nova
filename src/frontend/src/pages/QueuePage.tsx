import {
  Box, Typography, Table, TableBody, TableCell, TableContainer,
  TableHead, TableRow, Paper, Button, Skeleton, Tabs, Tab,
} from '@mui/material';
import ReplayIcon from '@mui/icons-material/Replay';
import CancelIcon from '@mui/icons-material/Cancel';
import { useState, useCallback } from 'react';
import { useLocation } from 'wouter';
import TopBar from '../components/shell/TopBar';
import ProjectStatusChip from '../components/common/ProjectStatusChip';
import EmptyState from '../components/common/EmptyState';
import RetryProjectDialog from '../components/projects/RetryProjectDialog';
import { useQueue } from '../hooks/useData';
import { projectsApi } from '../api';
import { useNotification } from '../components/notifications';
import { useSWRConfig } from 'swr';
import { formatDuration, formatDate, formatBytes } from '../utils/format';
import { formatEngineLabel } from '../utils/transcription';
import type { QueueItemDto } from '../types';

export default function QueuePage() {
  const { data: queue, isLoading } = useQueue();
  const [tab, setTab] = useState(0);
  const [retryProjectId, setRetryProjectId] = useState<string | null>(null);
  const [, navigate] = useLocation();
  const { notify } = useNotification();
  const { mutate } = useSWRConfig();

  const handleCancel = useCallback(async (id: string) => {
    try {
      await projectsApi.cancel(id);
      await mutate('queue');
      notify('Project cancelled');
    } catch {
      notify('Failed to cancel', 'error');
    }
  }, [mutate, notify]);

  const allItems = [
    ...(queue?.processing ?? []),
    ...(queue?.queued ?? []),
    ...(queue?.completed ?? []),
    ...(queue?.failed ?? []),
  ];

  const sections = [
    { label: 'All', items: allItems },
    { label: 'Processing', items: queue?.processing ?? [] },
    { label: 'Queued', items: queue?.queued ?? [] },
    { label: 'Completed', items: queue?.completed ?? [] },
    { label: 'Failed', items: queue?.failed ?? [] },
  ];

  const currentItems = sections[tab]?.items ?? [];

  return (
    <>
      <TopBar title="Queue" />

      {isLoading ? (
        <Skeleton variant="rounded" height={300} />
      ) : (
        <>
          <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ mb: 2 }}>
            {sections.map((s, i) => (
              <Tab key={i} label={`${s.label} (${s.items.length})`} />
            ))}
          </Tabs>

          {currentItems.length === 0 ? (
            <EmptyState
              title={sections[tab].label === 'All'
                ? 'No jobs'
                : `No ${sections[tab].label.toLowerCase()} jobs`}
            />
          ) : (
            <TableContainer component={Paper} variant="outlined">
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>Project</TableCell>
                    <TableCell>Folder</TableCell>
                    <TableCell>Status</TableCell>
                    <TableCell>Engine</TableCell>
                    <TableCell>Duration</TableCell>
                    <TableCell>Transcribed In</TableCell>
                    <TableCell>Size</TableCell>
                    <TableCell>Created</TableCell>
                    <TableCell>Actions</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {currentItems.map((item: QueueItemDto) => (
                    <TableRow
                      key={item.id}
                      hover
                      sx={{ cursor: 'pointer' }}
                      onClick={() => navigate(`/projects/${item.id}`)}
                    >
                      <TableCell>
                        <Typography variant="body2" fontWeight={500}>{item.name}</Typography>
                      </TableCell>
                      <TableCell>{item.folderName}</TableCell>
                      <TableCell>
                        <ProjectStatusChip status={item.status} />
                      </TableCell>
                      <TableCell>
                        <Typography variant="caption">{formatEngineLabel(item.engine)} / {item.model}</Typography>
                      </TableCell>
                      <TableCell>{formatDuration(item.durationMs)}</TableCell>
                      <TableCell>{formatDuration(item.transcriptionElapsedMs)}</TableCell>
                      <TableCell>{formatBytes(item.totalSizeBytes)}</TableCell>
                      <TableCell>{formatDate(item.createdAtUtc)}</TableCell>
                      <TableCell>
                        <Box sx={{ display: 'flex', gap: 0.5 }} onClick={(e) => e.stopPropagation()}>
                          {item.status === 'Failed' && (
                            <Button size="small" startIcon={<ReplayIcon />} onClick={() => setRetryProjectId(item.id)}>
                              Retry
                            </Button>
                          )}
                          {item.status === 'Queued' && (
                            <Button size="small" startIcon={<CancelIcon />} onClick={() => handleCancel(item.id)}>
                              Cancel
                            </Button>
                          )}
                          {(item.status === 'PreparingMedia' || item.status === 'Transcribing') && (
                            <Button size="small" color="warning" startIcon={<CancelIcon />} onClick={() => handleCancel(item.id)}>
                              Stop
                            </Button>
                          )}
                        </Box>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          )}
        </>
      )}

      {retryProjectId && (
        <RetryProjectDialog
          open
          projectId={retryProjectId}
          onClose={() => setRetryProjectId(null)}
        />
      )}
    </>
  );
}
