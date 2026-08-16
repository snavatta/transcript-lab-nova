import { Fragment, useMemo, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Paper,
  Skeleton,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tooltip,
  Typography,
} from '@mui/material';
import DownloadIcon from '@mui/icons-material/Download';
import ExtensionIcon from '@mui/icons-material/Extension';
import RefreshIcon from '@mui/icons-material/Refresh';
import ScienceIcon from '@mui/icons-material/Science';
import { useSWRConfig } from 'swr';
import { ApiError, settingsApi } from '../../api';
import { useNotification } from '../notifications';
import { useTranscriptionModels } from '../../hooks/useData';
import type { TranscriptionModelEntryDto } from '../../types';
import { formatEngineLabel } from '../../utils/transcription';

const probeStateColor: Record<string, 'default' | 'success' | 'warning' | 'error' | 'info'> = {
  Ready: 'success',
  Installed: 'info',
  Missing: 'default',
  Unavailable: 'warning',
  Unsupported: 'warning',
  Failed: 'error',
};

function getProbeChipColor(probeState: string) {
  return probeStateColor[probeState] ?? 'default';
}

function getRowKey(model: Pick<TranscriptionModelEntryDto, 'engine' | 'model'>) {
  return `${model.engine}:${model.model}`;
}

export function LocalModelManagerPanel() {
  const { data: modelCatalog, isLoading: modelsLoading } = useTranscriptionModels();
  const { notify } = useNotification();
  const { mutate } = useSWRConfig();
  const [modelActionKey, setModelActionKey] = useState<string | null>(null);
  const [modelError, setModelError] = useState<string | null>(null);
  const groupedModels = useMemo(() => {
    const map = new Map<string, TranscriptionModelEntryDto[]>();
    for (const model of modelCatalog?.models ?? []) {
      const group = map.get(model.engine) ?? [];
      group.push(model);
      map.set(model.engine, group);
    }
    return Array.from(map.entries());
  }, [modelCatalog]);

  const handleModelAction = async (
    model: TranscriptionModelEntryDto,
    action: 'Download' | 'Redownload' | 'Probe',
  ) => {
    const key = `${getRowKey(model)}:${action}`;
    setModelActionKey(key);
    setModelError(null);
    try {
      await settingsApi.manageModel({ engine: model.engine, model: model.model, action });
      await mutate('settings/models');
      notify(`${formatEngineLabel(model.engine)} ${model.model} ${action.toLowerCase()} completed`);
    } catch (error) {
      const message = error instanceof ApiError ? error.message : `Failed to ${action.toLowerCase()} model`;
      setModelError(message);
      notify(message, 'error');
    } finally {
      setModelActionKey(null);
    }
  };

  return (
    <Paper variant="outlined" sx={{ p: 3, minWidth: 0, width: '100%' }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 0.5 }}>
        <ExtensionIcon sx={{ fontSize: 20, color: 'text.secondary' }} />
        <Typography variant="subtitle1" fontWeight={600}>Model Manager</Typography>
      </Box>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        Download, probe, and manage local model installs. Models are probed automatically on page load so runtime problems are visible before you upload.
      </Typography>

      {modelError && (
        <Alert severity="error" variant="outlined" sx={{ mb: 2 }}>{modelError}</Alert>
      )}

      {modelsLoading && !modelCatalog ? (
        <Skeleton variant="rounded" height={320} />
      ) : (
        <>
          <Typography variant="caption" color="text.secondary" sx={{ display: { xs: 'block', sm: 'none' }, mb: 1 }}>
            Swipe horizontally to view model status and actions.
          </Typography>
          <TableContainer data-testid="model-manager-scroll" sx={{ overflowX: 'auto', pb: { xs: 1, sm: 0 } }}>
            <Table size="small" sx={{ minWidth: 600 }}>
              <TableHead>
                <TableRow>
                  <TableCell>Model</TableCell>
                  <TableCell>Filesystem</TableCell>
                  <TableCell>Probe</TableCell>
                  <TableCell align="right">Actions</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {groupedModels.map(([engine, models]) => (
                  <Fragment key={engine}>
                    <TableRow>
                      <TableCell colSpan={4} sx={{ bgcolor: 'action.hover', py: 0.75, borderBottom: 'none' }}>
                        <Typography variant="caption" fontWeight={600} color="text.secondary">
                          {formatEngineLabel(engine)}
                        </Typography>
                      </TableCell>
                    </TableRow>
                    {models.map((model) => {
                      const rowKey = getRowKey(model);
                      const isDownloading = modelActionKey === `${rowKey}:Download`;
                      const isRedownloading = modelActionKey === `${rowKey}:Redownload`;
                      const isProbing = modelActionKey === `${rowKey}:Probe`;

                      return (
                        <TableRow key={rowKey} hover>
                          <TableCell sx={{ whiteSpace: 'nowrap' }}>{model.model}</TableCell>
                          <TableCell sx={{ minWidth: 200, maxWidth: 280 }}>
                            <Stack spacing={0.5}>
                              <Chip
                                size="small"
                                variant="outlined"
                                label={model.isInstalled ? 'Installed' : 'Missing'}
                                color={model.isInstalled ? 'success' : 'default'}
                                sx={{ width: 'fit-content' }}
                              />
                              <Typography
                                variant="caption"
                                color="text.secondary"
                                title={model.installPath ?? undefined}
                                sx={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: 260, display: 'block' }}
                              >
                                {model.installPath ?? 'No install path available'}
                              </Typography>
                            </Stack>
                          </TableCell>
                          <TableCell sx={{ minWidth: 260 }}>
                            <Stack spacing={0.75} alignItems="flex-start">
                              <Chip
                                size="small"
                                label={model.probeState}
                                color={getProbeChipColor(model.probeState)}
                                variant={model.probeState === 'Ready' ? 'filled' : 'outlined'}
                              />
                              <Typography variant="caption" color="text.secondary">{model.probeMessage}</Typography>
                            </Stack>
                          </TableCell>
                          <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                            <Stack direction="row" spacing={1} justifyContent="flex-end">
                              {model.canRedownload ? (
                                <Tooltip title="Re-download to replace the current local copy">
                                  <span>
                                    <Button
                                      size="small"
                                      variant="outlined"
                                      startIcon={isRedownloading ? <CircularProgress size={14} /> : <RefreshIcon fontSize="small" />}
                                      disabled={modelActionKey !== null}
                                      onClick={() => handleModelAction(model, 'Redownload')}
                                    >
                                      Redownload
                                    </Button>
                                  </span>
                                </Tooltip>
                              ) : (
                                <Tooltip title="Download this model to local storage">
                                  <span>
                                    <Button
                                      size="small"
                                      variant="outlined"
                                      startIcon={isDownloading ? <CircularProgress size={14} /> : <DownloadIcon fontSize="small" />}
                                      disabled={!model.canDownload || modelActionKey !== null}
                                      onClick={() => handleModelAction(model, 'Download')}
                                    >
                                      Download
                                    </Button>
                                  </span>
                                </Tooltip>
                              )}
                              <Tooltip title="Run a live probe to verify the model loads correctly">
                                <span>
                                  <Button
                                    size="small"
                                    variant="outlined"
                                    startIcon={isProbing ? <CircularProgress size={14} /> : <ScienceIcon fontSize="small" />}
                                    disabled={!model.canProbe || modelActionKey !== null}
                                    onClick={() => handleModelAction(model, 'Probe')}
                                  >
                                    Probe
                                  </Button>
                                </span>
                              </Tooltip>
                            </Stack>
                          </TableCell>
                        </TableRow>
                      );
                    })}
                  </Fragment>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        </>
      )}
    </Paper>
  );
}
