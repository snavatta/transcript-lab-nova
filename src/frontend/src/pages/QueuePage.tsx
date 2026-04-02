import {
  Box, Typography, Table, TableBody, TableCell, TableContainer,
  TableHead, TableRow, Paper, LinearProgress, Button, Skeleton, Tabs, Tab,
} from '@mui/material';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import ReplayIcon from '@mui/icons-material/Replay';
import CancelIcon from '@mui/icons-material/Cancel';
import { useState, useCallback } from 'react';
import { useLocation } from 'wouter';
import TopBar from '../components/shell/TopBar';
import ProjectStatusChip from '../components/common/ProjectStatusChip';
import EmptyState from '../components/common/EmptyState';
import { useQueue } from '../hooks/useData';
import { projectsApi } from '../api';
import { useNotification } from '../components/notifications';
import { useSWRConfig } from 'swr';
import { formatDuration, formatDate, formatBytes } from '../utils/format';
import type { QueueItemDto } from '../types';

export default function QueuePage() {
  const { data: queue, isLoading } = useQueue();
  const [tab, setTab] = useState(0);
  const [, navigate] = useLocation();
  const { notify } = useNotification();
  const { mutate } = useSWRConfig();

  const handleQueue = useCallback(async (id: string) => {
    try {
      await projectsApi.queue(id);
      await mutate('queue');
      notify('Project queued');
    } catch {
      notify('Failed to queue', 'error');
    }
  }, [mutate, notify]);

  const handleRetry = useCallback(async (id: string) => {
    try {
      await projectsApi.retry(id);
      await mutate('queue');
      notify('Project re-queued');
    } catch {
      notify('Failed to retry', 'error');
    }
  }, [mutate, notify]);

  const handleCancel = useCallback(async (id: string) => {
    try {
      await projectsApi.cancel(id);
      await mutate('queue');
      notify('Project cancelled');
    } catch {
      notify('Failed to cancel', 'error');
    }
  }, [mutate, notify]);

  const sections = [
    { label: 'Drafts', items: queue?.drafts ?? [] },
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
            <EmptyState title={`No ${sections[tab].label.toLowerCase()} jobs`} />
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
                        <Box>
                          <ProjectStatusChip status={item.status} />
                          {(item.status === 'PreparingMedia' || item.status === 'Transcribing') && item.progress != null && (
                            <LinearProgress variant="determinate" value={item.progress} sx={{ mt: 0.5, width: 80 }} />
                          )}
                        </Box>
                      </TableCell>
                      <TableCell>
                        <Typography variant="caption">{item.engine} / {item.model}</Typography>
                      </TableCell>
                      <TableCell>{formatDuration(item.durationMs)}</TableCell>
                      <TableCell>{formatBytes(item.totalSizeBytes)}</TableCell>
                      <TableCell>{formatDate(item.createdAtUtc)}</TableCell>
                      <TableCell>
                        <Box sx={{ display: 'flex', gap: 0.5 }} onClick={(e) => e.stopPropagation()}>
                          {item.status === 'Draft' && (
                            <Button size="small" startIcon={<PlayArrowIcon />} onClick={() => handleQueue(item.id)}>
                              Queue
                            </Button>
                          )}
                          {item.status === 'Failed' && (
                            <Button size="small" startIcon={<ReplayIcon />} onClick={() => handleRetry(item.id)}>
                              Retry
                            </Button>
                          )}
                          {item.status === 'Queued' && (
                            <Button size="small" startIcon={<CancelIcon />} onClick={() => handleCancel(item.id)}>
                              Cancel
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
    </>
  );
}
