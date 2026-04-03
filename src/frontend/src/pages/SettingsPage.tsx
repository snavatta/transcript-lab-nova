import { useEffect, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Divider,
  FormControl,
  FormControlLabel,
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
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import SaveIcon from '@mui/icons-material/Save';
import RestoreIcon from '@mui/icons-material/Restore';
import DownloadIcon from '@mui/icons-material/Download';
import RefreshIcon from '@mui/icons-material/Refresh';
import ScienceIcon from '@mui/icons-material/Science';
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
        <Skeleton variant="rounded" height={400} />
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

      <Box
        sx={{
          display: 'flex',
          flexDirection: 'column',
          gap: 3,
          alignItems: 'start',
        }}
      >
        <Paper variant="outlined" sx={{ p: 3, width: '100%' }}>
          <Typography variant="subtitle1" gutterBottom fontWeight={600}>
            Default Transcription Settings
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
            These defaults apply to new uploads only. Existing projects are not affected.
          </Typography>

          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2.5 }}>
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

            <Divider />

            <FormControlLabel
              control={
                <Switch
                  checked={form.defaultAudioNormalizationEnabled}
                  onChange={(e) => setForm({ ...form, defaultAudioNormalizationEnabled: e.target.checked })}
                />
              }
              label="Audio Normalization"
            />

            <FormControlLabel
              control={
                <Switch
                  checked={form.defaultDiarizationEnabled}
                  onChange={(e) => setForm({ ...form, defaultDiarizationEnabled: e.target.checked })}
                />
              }
              label="Speaker Diarization"
            />

            <Divider />

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
          <Stack spacing={1.5} sx={{ mb: 2 }}>
            <Typography variant="subtitle1" fontWeight={600}>
              Model Manager
            </Typography>
            <Typography variant="body2" color="text.secondary">
              Download, refresh, and actively probe local model installs before running a real project.
            </Typography>
            <Typography variant="caption" color="text.secondary">
              Installed models are probed automatically when this page loads. That makes runtime failures visible here instead of only during uploads.
            </Typography>
          </Stack>

          {modelError && (
            <Alert severity="error" variant="outlined" sx={{ mb: 2 }}>
              {modelError}
            </Alert>
          )}

          {modelsLoading && !modelCatalog ? (
            <Skeleton variant="rounded" height={420} />
          ) : (
            <Box sx={{ overflowX: 'auto' }}>
              <Table size="small" sx={{ minWidth: 760 }}>
                <TableHead>
                  <TableRow>
                    <TableCell>Engine</TableCell>
                    <TableCell>Model</TableCell>
                    <TableCell>Filesystem</TableCell>
                    <TableCell>Probe</TableCell>
                    <TableCell align="right">Actions</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {modelCatalog?.models.map((model) => {
                    const rowKey = getRowKey(model);
                    const isDownloading = modelActionKey === `${rowKey}:Download`;
                    const isRedownloading = modelActionKey === `${rowKey}:Redownload`;
                    const isProbing = modelActionKey === `${rowKey}:Probe`;

                    return (
                      <TableRow key={rowKey} hover>
                        <TableCell sx={{ whiteSpace: 'nowrap' }}>{formatEngineLabel(model.engine)}</TableCell>
                        <TableCell sx={{ whiteSpace: 'nowrap' }}>{model.model}</TableCell>
                        <TableCell sx={{ minWidth: 220 }}>
                          <Stack spacing={0.5}>
                            <Chip
                              size="small"
                              variant="outlined"
                              label={model.isInstalled ? 'Installed' : 'Missing'}
                              color={model.isInstalled ? 'success' : 'default'}
                              sx={{ width: 'fit-content' }}
                            />
                            <Typography variant="caption" color="text.secondary" sx={{ wordBreak: 'break-all' }}>
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
                            <Button
                              size="small"
                              variant="outlined"
                              startIcon={isDownloading ? <CircularProgress size={14} /> : <DownloadIcon fontSize="small" />}
                              disabled={!model.canDownload || modelActionKey !== null}
                              onClick={() => handleModelAction(model, 'Download')}
                            >
                              Download
                            </Button>
                            <Button
                              size="small"
                              variant="outlined"
                              startIcon={isRedownloading ? <CircularProgress size={14} /> : <RefreshIcon fontSize="small" />}
                              disabled={!model.canRedownload || modelActionKey !== null}
                              onClick={() => handleModelAction(model, 'Redownload')}
                            >
                              Redownload
                            </Button>
                            <Button
                              size="small"
                              variant="outlined"
                              startIcon={isProbing ? <CircularProgress size={14} /> : <ScienceIcon fontSize="small" />}
                              disabled={!model.canProbe || modelActionKey !== null}
                              onClick={() => handleModelAction(model, 'Probe')}
                            >
                              Probe
                            </Button>
                          </Stack>
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </Box>
          )}
        </Paper>
      </Box>
    </>
  );
}
