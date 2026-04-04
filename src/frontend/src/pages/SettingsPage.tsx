import { Fragment, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Divider,
  FormControl,
  InputLabel,
  MenuItem,
  Paper,
  Select,
  Skeleton,
  Stack,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tooltip,
  Typography,
} from '@mui/material';
import SaveIcon from '@mui/icons-material/Save';
import RestoreIcon from '@mui/icons-material/Restore';
import DownloadIcon from '@mui/icons-material/Download';
import RefreshIcon from '@mui/icons-material/Refresh';
import ScienceIcon from '@mui/icons-material/Science';
import TuneIcon from '@mui/icons-material/Tune';
import ExtensionIcon from '@mui/icons-material/Extension';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import TopBar from '../components/shell/TopBar';
import { useSettings, useTranscriptionModels, useTranscriptionOptions } from '../hooks/useData';
import { ApiError, settingsApi } from '../api';
import { useNotification } from '../components/notifications';
import { useSWRConfig } from 'swr';
import type {
  TranscriptionModelEntryDto,
  UpdateGlobalSettingsRequest,
  TranscriptViewMode,
  LanguageMode,
} from '../types';
import { formatEngineLabel } from '../utils/transcription';
import { coerceFixedLanguageCodeForEngine, getLanguageOptionsForEngine } from '../utils/languages';
import { DIARIZATION_MODES } from '../config/diarizationOptions';

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

export default function SettingsPage() {
  const { data: settings, isLoading } = useSettings();
  const { data: transcriptionOptions } = useTranscriptionOptions();
  const { data: modelCatalog, isLoading: modelsLoading } = useTranscriptionModels();
  const { notify } = useNotification();
  const { mutate } = useSWRConfig();
  const [form, setForm] = useState<UpdateGlobalSettingsRequest | null>(null);
  const [saving, setSaving] = useState(false);
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

  useEffect(() => {
    if (settings && !form) {
      setForm({
        defaultEngine: settings.defaultEngine,
        defaultModel: settings.defaultModel,
        defaultLanguageMode: settings.defaultLanguageMode,
        defaultLanguageCode: settings.defaultLanguageMode === 'Fixed'
          ? coerceFixedLanguageCodeForEngine(settings.defaultEngine, settings.defaultLanguageCode)
          : null,
        defaultAudioNormalizationEnabled: settings.defaultAudioNormalizationEnabled,
        defaultDiarizationEnabled: settings.defaultDiarizationEnabled,
        defaultDiarizationMode: settings.defaultDiarizationMode ?? 'Basic',
        defaultTranscriptViewMode: settings.defaultTranscriptViewMode,
      });
    }
  }, [settings, form]);

  const handleSave = async () => {
    if (!form) return;
    setSaving(true);
    try {
      await settingsApi.update(form);
      await mutate('settings');
      notify('Settings saved');
    } catch {
      notify('Failed to save settings', 'error');
    } finally {
      setSaving(false);
    }
  };

  const handleReset = () => {
    if (settings) {
      setForm({
        defaultEngine: settings.defaultEngine,
        defaultModel: settings.defaultModel,
        defaultLanguageMode: settings.defaultLanguageMode,
        defaultLanguageCode: settings.defaultLanguageMode === 'Fixed'
          ? coerceFixedLanguageCodeForEngine(settings.defaultEngine, settings.defaultLanguageCode)
          : null,
        defaultAudioNormalizationEnabled: settings.defaultAudioNormalizationEnabled,
        defaultDiarizationEnabled: settings.defaultDiarizationEnabled,
        defaultDiarizationMode: settings.defaultDiarizationMode ?? 'Basic',
        defaultTranscriptViewMode: settings.defaultTranscriptViewMode,
      });
    }
  };

  const handleModelAction = async (model: TranscriptionModelEntryDto, action: 'Download' | 'Redownload' | 'Probe') => {
    const key = `${getRowKey(model)}:${action}`;
    setModelActionKey(key);
    setModelError(null);
    try {
      await settingsApi.manageModel({
        engine: model.engine,
        model: model.model,
        action,
      });
      await mutate('settings/models');
      notify(`${formatEngineLabel(model.engine)} ${model.model} ${action.toLowerCase()} completed`);
    } catch (err) {
      const message = err instanceof ApiError ? err.message : `Failed to ${action.toLowerCase()} model`;
      setModelError(message);
      notify(message, 'error');
    } finally {
      setModelActionKey(null);
    }
  };

  const handleEngineChange = (engine: string) => {
    const models = transcriptionOptions?.engines.find((option) => option.engine === engine)?.models
      ?? [form?.defaultModel ?? 'small'];
    setForm((current) => current
      ? {
          ...current,
          defaultEngine: engine,
          defaultModel: models.includes(current.defaultModel) ? current.defaultModel : models[0],
          defaultLanguageCode: current.defaultLanguageMode === 'Fixed'
            ? coerceFixedLanguageCodeForEngine(engine, current.defaultLanguageCode)
            : null,
        }
      : current);
  };

  const handleLanguageModeChange = (languageMode: LanguageMode) => {
    setForm((current) => current
      ? {
          ...current,
          defaultLanguageMode: languageMode,
          defaultLanguageCode: languageMode === 'Fixed'
            ? coerceFixedLanguageCodeForEngine(current.defaultEngine, current.defaultLanguageCode)
            : null,
        }
      : current);
  };

  if (isLoading || !form) {
    return (
      <>
        <TopBar title="Settings" />
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
          <Skeleton variant="rounded" height={360} />
          <Skeleton variant="rounded" height={460} />
        </Box>
      </>
    );
  }

  const engineOptions = transcriptionOptions?.engines
    ?? [{ engine: form.defaultEngine, models: [form.defaultModel] }];
  const modelOptions = engineOptions.find((option) => option.engine === form.defaultEngine)?.models
    ?? [form.defaultModel];
  const languageOptions = getLanguageOptionsForEngine(form.defaultEngine);

  return (
    <>
      <TopBar
        title="Settings"
        actions={
          <>
            <Button variant="outlined" startIcon={<RestoreIcon />} onClick={handleReset} disabled={saving}>
              Reset
            </Button>
            <Button variant="contained" startIcon={<SaveIcon />} onClick={handleSave} disabled={saving}>
              Save
            </Button>
          </>
        }
      />

      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
        <Paper variant="outlined" sx={{ p: 3, width: '100%' }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 0.5 }}>
            <TuneIcon sx={{ fontSize: 20, color: 'text.secondary' }} />
            <Typography variant="subtitle1" fontWeight={600}>
              Default Transcription Settings
            </Typography>
          </Box>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
            These defaults apply to new uploads only. Existing projects are not affected.
          </Typography>

          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2.5 }}>
            <Box
              sx={{
                display: 'grid',
                gridTemplateColumns: { xs: '1fr', md: 'repeat(2, minmax(0, 1fr))' },
                gap: 2,
              }}
            >
              <FormControl fullWidth>
                <InputLabel>Engine</InputLabel>
                <Select
                  value={form.defaultEngine}
                  label="Engine"
                  onChange={(e) => handleEngineChange(e.target.value)}
                >
                  {engineOptions.map((option) => (
                    <MenuItem key={option.engine} value={option.engine}>{formatEngineLabel(option.engine)}</MenuItem>
                  ))}
                </Select>
              </FormControl>

              <FormControl fullWidth>
                <InputLabel>Model</InputLabel>
                <Select
                  value={form.defaultModel}
                  label="Model"
                  onChange={(e) => setForm({ ...form, defaultModel: e.target.value })}
                >
                  {modelOptions.map((m) => (
                    <MenuItem key={m} value={m}>{m}</MenuItem>
                  ))}
                </Select>
              </FormControl>
            </Box>

            {(form.defaultEngine === 'OpenVinoWhisperSidecar' || form.defaultEngine === 'OpenAiCompatible') && (
              <Box
                sx={{
                  display: 'flex',
                  alignItems: 'flex-start',
                  gap: 1,
                  p: 1.5,
                  borderRadius: 1,
                  bgcolor: 'action.hover',
                }}
              >
                <InfoOutlinedIcon sx={{ fontSize: 16, mt: 0.25, color: 'text.secondary', flexShrink: 0 }} />
                <Typography variant="caption" color="text.secondary">
                  {form.defaultEngine === 'OpenVinoWhisperSidecar'
                    ? 'Uses a local OpenVINO GPU sidecar. Requires the OpenVINO Python environment to be configured.'
                    : 'Requires backend configuration in appsettings.json. Contact your administrator to configure the target URL and model.'}
                </Typography>
              </Box>
            )}

            <Box
              sx={{
                display: 'grid',
                gridTemplateColumns: { xs: '1fr', md: 'repeat(2, minmax(0, 1fr))' },
                gap: 2,
              }}
            >
              <FormControl fullWidth>
                <InputLabel>Language Mode</InputLabel>
                <Select
                  value={form.defaultLanguageMode}
                  label="Language Mode"
                  onChange={(e) => handleLanguageModeChange(e.target.value as LanguageMode)}
                >
                  <MenuItem value="Auto">Auto-detect</MenuItem>
                  <MenuItem value="Fixed">Fixed</MenuItem>
                </Select>
              </FormControl>

              {form.defaultLanguageMode === 'Fixed' && (
                <FormControl fullWidth>
                  <InputLabel>Fixed Language</InputLabel>
                  <Select
                    value={coerceFixedLanguageCodeForEngine(form.defaultEngine, form.defaultLanguageCode)}
                    label="Fixed Language"
                    onChange={(e) => setForm({ ...form, defaultLanguageCode: e.target.value })}
                  >
                    {languageOptions.map((option) => (
                      <MenuItem key={option.code} value={option.code}>
                        {option.label} ({option.code})
                      </MenuItem>
                    ))}
                  </Select>
                </FormControl>
              )}
            </Box>

            <Divider />

            <Typography variant="subtitle2" color="text.secondary">
              Processing Options
            </Typography>

            <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 2 }}>
              <Box>
                <Typography variant="body2" fontWeight={500}>Audio Normalization</Typography>
                <Typography variant="caption" color="text.secondary">
                  Levels audio volume before transcription for improved accuracy on quiet or variable recordings.
                </Typography>
              </Box>
              <Switch
                checked={form.defaultAudioNormalizationEnabled}
                onChange={(e) => setForm({ ...form, defaultAudioNormalizationEnabled: e.target.checked })}
                inputProps={{ 'aria-label': 'Audio Normalization' }}
              />
            </Box>

            <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 2 }}>
              <Box>
                <Typography variant="body2" fontWeight={500}>Speaker Diarization</Typography>
                <Typography variant="caption" color="text.secondary">
                  Identifies and separates individual speakers in the transcript.
                </Typography>
              </Box>
              <Switch
                checked={form.defaultDiarizationEnabled}
                onChange={(e) => setForm({ ...form, defaultDiarizationEnabled: e.target.checked })}
                inputProps={{ 'aria-label': 'Speaker Diarization' }}
              />
            </Box>

            {form.defaultDiarizationEnabled && (
              <FormControl fullWidth>
                <InputLabel>Default Diarization Mode</InputLabel>
                <Select
                  value={form.defaultDiarizationMode}
                  label="Default Diarization Mode"
                  onChange={(e) => setForm({ ...form, defaultDiarizationMode: e.target.value })}
                >
                  {DIARIZATION_MODES.map((option) => (
                    <MenuItem key={option.value} value={option.value}>
                      <Box>
                        <Typography variant="body2">{option.label}</Typography>
                        <Typography variant="caption" color="text.secondary">{option.description}</Typography>
                      </Box>
                    </MenuItem>
                  ))}
                </Select>
              </FormControl>
            )}

            <Divider />

            <Typography variant="subtitle2" color="text.secondary">
              Display Options
            </Typography>

            <FormControl fullWidth>
              <InputLabel>Default Transcript View</InputLabel>
              <Select
                value={form.defaultTranscriptViewMode}
                label="Default Transcript View"
                onChange={(e) => setForm({ ...form, defaultTranscriptViewMode: e.target.value as TranscriptViewMode })}
              >
                <MenuItem value="Readable">Readable</MenuItem>
                <MenuItem value="Timestamped">Timestamped</MenuItem>
              </Select>
            </FormControl>
          </Box>
        </Paper>

        <Paper variant="outlined" sx={{ p: 3, minWidth: 0, width: '100%' }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 0.5 }}>
            <ExtensionIcon sx={{ fontSize: 20, color: 'text.secondary' }} />
            <Typography variant="subtitle1" fontWeight={600}>
              Model Manager
            </Typography>
          </Box>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            Download, probe, and manage local model installs. Models are probed automatically on page load so runtime problems are visible before you upload.
          </Typography>

          {modelError && (
            <Alert severity="error" variant="outlined" sx={{ mb: 2 }}>
              {modelError}
            </Alert>
          )}

          {modelsLoading && !modelCatalog ? (
            <Skeleton variant="rounded" height={320} />
          ) : (
            <TableContainer>
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
                        <TableCell
                          colSpan={4}
                          sx={{
                            bgcolor: 'action.hover',
                            py: 0.75,
                            borderBottom: 'none',
                          }}
                        >
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
                              sx={{
                                overflow: 'hidden',
                                textOverflow: 'ellipsis',
                                whiteSpace: 'nowrap',
                                maxWidth: 260,
                                display: 'block',
                              }}
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
                            <Typography variant="caption" color="text.secondary">
                              {model.probeMessage}
                            </Typography>
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
          )}
        </Paper>
      </Box>
    </>
  );
}
