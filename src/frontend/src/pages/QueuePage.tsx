import {
  Box, Typography, Table, TableBody, TableCell, TableContainer,
  TableHead, TableRow, Paper, Button, Skeleton, Tabs, Tab,
  Chip,
  Stack,
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
import { useIsMobile } from '../hooks/useIsMobile';

export default function QueuePage() {
  const isMobile = useIsMobile();
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
          <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ mb: 2 }} variant="scrollable" allowScrollButtonsMobile>
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
          ) : isMobile ? (
            <Stack spacing={1.5}>
              {currentItems.map((item: QueueItemDto) => (
                <Paper
                  key={item.id}
                  variant="outlined"
                  sx={{ p: 2, cursor: 'pointer' }}
                  onClick={() => navigate(`/projects/${item.id}`)}
                >
                  <Stack spacing={1.25}>
                    <Box sx={{ minWidth: 0 }}>
                      <Typography variant="subtitle2" noWrap>{item.name}</Typography>
                      <Typography variant="caption" color="text.secondary">{item.folderName}</Typography>
                    </Box>

                    <Stack direction="row" spacing={1} useFlexGap flexWrap="wrap">
                      <ProjectStatusChip status={item.status} />
                      <Chip size="small" label={`${formatEngineLabel(item.engine)} / ${item.model}`} variant="outlined" />
                      <Chip size="small" label={`Duration: ${formatDuration(item.durationMs)}`} variant="outlined" />
                      <Chip size="small" label={`Transcribed: ${formatDuration(item.transcriptionElapsedMs)}`} variant="outlined" />
                      <Chip size="small" label={`Size: ${formatBytes(item.totalSizeBytes)}`} variant="outlined" />
                    </Stack>

                    <Typography variant="caption" color="text.secondary">
                      Created {formatDate(item.createdAtUtc)}
                    </Typography>

                    <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }} onClick={(event) => event.stopPropagation()}>
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
                  </Stack>
                </Paper>
              ))}
            </Stack>
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
