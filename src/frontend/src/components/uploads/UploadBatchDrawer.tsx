import { useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Divider,
  Drawer,
  FormControl,
  FormControlLabel,
  InputLabel,
  List,
  ListItem,
  ListItemText,
  MenuItem,
  Select,
  Switch,
  TextField,
  Typography,
} from '@mui/material';
import { useSWRConfig } from 'swr';
import { ApiError, uploadsApi } from '../../api';
import { useSettings, useTranscriptionOptions } from '../../hooks/useData';
import type { LanguageMode, ProjectSettingsDto, TranscriptionEngineOptionDto, TranscriptionOptionsDto } from '../../types';
import { formatEngineLabel } from '../../utils/transcription';
import { coerceFixedLanguageCodeForEngine, getLanguageOptionsForEngine } from '../../utils/languages';
import { useNotification } from '../notifications';
import {
  DIARIZATION_MODES,
  coerceDiarizationSource,
  getDefaultDiarizationSource,
  supportsProviderDiarization,
  supportsXaiDiarization,
  XAI_DIARIZATION_DISCLOSURE,
} from '../../config/diarizationOptions';

interface Props {
  open: boolean;
  folderId: string;
  files: File[];
  onClose: () => void;
}

interface UploadItemForm {
  originalFileName: string;
  projectName: string;
}

function normalizeLanguageMode(languageMode: string): LanguageMode {
  return languageMode === 'Fixed' ? 'Fixed' : 'Auto';
}

function fileNameToProjectName(fileName: string): string {
  const lastDotIndex = fileName.lastIndexOf('.');
  return lastDotIndex > 0 ? fileName.slice(0, lastDotIndex) : fileName;
}

function createItems(files: File[]): UploadItemForm[] {
  return files.map((file) => ({
    originalFileName: file.name,
    projectName: fileNameToProjectName(file.name),
  }));
}

function createDefaultSettings(
  defaults: {
    defaultEngine: string;
    defaultModel: string;
    defaultLanguageMode: string;
    defaultLanguageCode: string | null;
    defaultAudioNormalizationEnabled: boolean;
    defaultDiarizationEnabled: boolean;
    defaultDiarizationSource: ProjectSettingsDto['diarizationSource'];
    defaultDiarizationMode: string;
    defaultSpeakerRoleAttributionEnabled: boolean;
  },
  engineOptions: TranscriptionEngineOptionDto[],
  transcriptionOptions: TranscriptionOptionsDto | undefined,
): ProjectSettingsDto {
  const fallbackEngine = engineOptions[0]?.engine ?? defaults.defaultEngine;
  const engine = engineOptions.some((option) => option.engine === defaults.defaultEngine)
    ? defaults.defaultEngine
    : fallbackEngine;
  const modelOptions = engineOptions.find((option) => option.engine === engine)?.models ?? [defaults.defaultModel];
  const model = modelOptions.includes(defaults.defaultModel) ? defaults.defaultModel : (modelOptions[0] ?? defaults.defaultModel);
  const languageMode = normalizeLanguageMode(defaults.defaultLanguageMode);

  return {
    engine,
    model,
    languageMode,
    languageCode: languageMode === 'Fixed'
      ? coerceFixedLanguageCodeForEngine(engine, defaults.defaultLanguageCode)
      : null,
    audioNormalizationEnabled: defaults.defaultAudioNormalizationEnabled,
    diarizationEnabled: defaults.defaultDiarizationEnabled,
    diarizationSource: coerceDiarizationSource(
      transcriptionOptions,
      defaults.defaultDiarizationSource,
      engine,
      model,
    ),
    diarizationMode: defaults.defaultDiarizationMode ?? 'Basic',
    speakerRoleAttributionEnabled: defaults.defaultSpeakerRoleAttributionEnabled,
  };
}

export default function UploadBatchDrawer({
  open,
  folderId,
  files,
  onClose,
}: Props) {
  const { data: settings, isLoading: settingsLoading } = useSettings();
  const { data: transcriptionOptions, isLoading: optionsLoading } = useTranscriptionOptions();
  const { mutate } = useSWRConfig();
  const { notify } = useNotification();
  const [items, setItems] = useState<UploadItemForm[]>([]);
  const [autoQueue, setAutoQueue] = useState(true);
  const [form, setForm] = useState<ProjectSettingsDto | null>(null);
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);

  const engineOptions = useMemo(() => {
    if (transcriptionOptions?.engines?.length) {
      return transcriptionOptions.engines;
    }

    if (!settings) {
      return [];
    }

    return [{ engine: settings.defaultEngine, models: [settings.defaultModel], providerDiarizationModels: [], wordTimestampModels: [] }];
  }, [settings, transcriptionOptions]);

  useEffect(() => {
    if (!open) {
      return;
    }

    setItems(createItems(files));
    setAutoQueue(true);
    setError('');

    if (settings && engineOptions.length > 0) {
      setForm(createDefaultSettings(settings, engineOptions, transcriptionOptions));
    }
  }, [open, files, settings, engineOptions, transcriptionOptions]);

  const modelOptions = form
    ? (engineOptions.find((option) => option.engine === form.engine)?.models ?? [form.model])
    : [];
  const languageOptions = form ? getLanguageOptionsForEngine(form.engine) : [];
  const providerDiarizationSupported = form
    ? supportsProviderDiarization(transcriptionOptions, form.engine, form.model)
    : false;
  const xaiDiarizationSupported = form
    ? supportsXaiDiarization(transcriptionOptions, form.engine, form.model)
    : false;

  const handleItemNameChange = (index: number, projectName: string) => {
    setItems((current) => current.map((item, itemIndex) => (
      itemIndex === index ? { ...item, projectName } : item
    )));
  };

  const handleEngineChange = (engine: string) => {
    const models = engineOptions.find((option) => option.engine === engine)?.models ?? [];
    setForm((current) => current
      ? (() => {
        const model = models.includes(current.model) ? current.model : (models[0] ?? current.model);
        return {
          ...current,
          engine,
          model,
          diarizationSource: getDefaultDiarizationSource(transcriptionOptions, engine, model),
          languageCode: current.languageMode === 'Fixed'
            ? coerceFixedLanguageCodeForEngine(engine, current.languageCode)
            : null,
        };
      })()
      : current);
    setError('');
  };

  const handleLanguageModeChange = (languageMode: LanguageMode) => {
    setForm((current) => current
      ? {
          ...current,
          languageMode,
          languageCode: languageMode === 'Fixed'
            ? coerceFixedLanguageCodeForEngine(current.engine, current.languageCode)
            : null,
        }
      : current);
    setError('');
  };

  const handleSubmit = async () => {
    if (!form) {
      return;
    }

    const normalizedItems = items.map((item) => ({
      originalFileName: item.originalFileName,
      projectName: item.projectName.trim() || fileNameToProjectName(item.originalFileName),
    }));

    setSaving(true);
    setError('');
    try {
      const result = await uploadsApi.batch({
        folderId,
        autoQueue,
        settings: form,
        files,
        items: normalizedItems,
      });

      await mutate((key: unknown) => typeof key === 'string' && key.startsWith('projects?'), undefined);
      await mutate(`folders/${folderId}`);
      await mutate('folders');
      await mutate('queue');

      notify(
        autoQueue
          ? `${result.createdProjects.length} project${result.createdProjects.length === 1 ? '' : 's'} queued`
          : `${result.createdProjects.length} project${result.createdProjects.length === 1 ? '' : 's'} created`,
      );
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to upload files');
    } finally {
      setSaving(false);
    }
  };

  return (
    <Drawer
      anchor="right"
      open={open}
      onClose={saving ? undefined : onClose}
      PaperProps={{
        role: 'dialog',
        'aria-modal': true,
        'aria-labelledby': 'batch-upload-title',
        sx: { width: { xs: '100%', sm: 560 } },
      }}
    >
      <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
        <Box
          sx={{
            px: { xs: 2, sm: 3 },
            py: 2.5,
            pl: { xs: 'calc(16px + var(--safe-area-left))', sm: 3 },
            pr: { xs: 'calc(16px + var(--safe-area-right))', sm: 3 },
          }}
        >
          <Typography id="batch-upload-title" variant="h6">Review Batch Upload</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mt: 0.75 }}>
            Confirm project names and transcription settings before creating projects in this folder.
          </Typography>
        </Box>

        <Divider />

        <Box
          sx={{
            flex: 1,
            overflowY: 'auto',
            px: { xs: 2, sm: 3 },
            py: 2.5,
            pl: { xs: 'calc(16px + var(--safe-area-left))', sm: 3 },
            pr: { xs: 'calc(16px + var(--safe-area-right))', sm: 3 },
            display: 'flex',
            flexDirection: 'column',
            gap: 3,
          }}
        >
          {error && <Alert severity="error">{error}</Alert>}

          {(settingsLoading || optionsLoading || !form) ? (
            <Typography variant="body2" color="text.secondary">
              Loading upload settings...
            </Typography>
          ) : (
            <>
              <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                <Typography variant="subtitle2" fontWeight={600}>
                  Files
                </Typography>

                <List disablePadding sx={{ border: '1px solid', borderColor: 'divider', borderRadius: 1 }}>
                  {items.map((item, index) => (
                    <ListItem
                      key={item.originalFileName}
                      divider={index < items.length - 1}
                      sx={{ alignItems: 'flex-start', flexDirection: 'column', gap: 1.25, py: 1.5 }}
                    >
                      <ListItemText
                        primary={item.originalFileName}
                        secondary="Project name"
                        sx={{ m: 0, width: '100%' }}
                      />
                      <TextField
                        fullWidth
                        size="small"
                        label="Project Name"
                        value={item.projectName}
                        onChange={(event) => handleItemNameChange(index, event.target.value)}
                      />
                    </ListItem>
                  ))}
                </List>
              </Box>

              <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                <Typography variant="subtitle2" fontWeight={600}>
                  Transcription Settings
                </Typography>

                <FormControl fullWidth>
                  <InputLabel id="upload-engine-label">Engine</InputLabel>
                  <Select
                    labelId="upload-engine-label"
                    value={form.engine}
                    label="Engine"
                    onChange={(event) => handleEngineChange(event.target.value)}
                  >
                    {engineOptions.map((option) => (
                      <MenuItem key={option.engine} value={option.engine}>
                        {formatEngineLabel(option.engine)}
                      </MenuItem>
                    ))}
                  </Select>
                </FormControl>

                {form.engine === 'Xai' && (
                  <Alert severity="info">The entire prepared audio file is sent directly to xAI. Direct xAI is recommended for long classes.</Alert>
                )}
                {form.engine === 'OpenRouter' && (
                  <Alert severity="info">Audio is sent through OpenRouter to the selected transcription provider.</Alert>
                )}

                <FormControl fullWidth>
                  <InputLabel id="upload-model-label">Model</InputLabel>
                  <Select
                    labelId="upload-model-label"
                    value={form.model}
                    label="Model"
                    onChange={(event) => {
                      const model = event.target.value;
                      setForm({
                        ...form,
                        model,
                        diarizationSource: getDefaultDiarizationSource(
                          transcriptionOptions,
                          form.engine,
                          model,
                        ),
                      });
                      setError('');
                    }}
                  >
                    {modelOptions.map((model) => (
                      <MenuItem key={model} value={model}>{model}</MenuItem>
                    ))}
                  </Select>
                </FormControl>

                <FormControl fullWidth>
                  <InputLabel id="upload-language-mode-label">Language Mode</InputLabel>
                  <Select
                    labelId="upload-language-mode-label"
                    value={form.languageMode}
                    label="Language Mode"
                    onChange={(event) => handleLanguageModeChange(event.target.value as LanguageMode)}
                  >
                    <MenuItem value="Auto">Auto-detect</MenuItem>
                    <MenuItem value="Fixed">Fixed</MenuItem>
                  </Select>
                </FormControl>

                {form.languageMode === 'Fixed' && (
                  <FormControl fullWidth>
                    <InputLabel id="upload-fixed-language-label">Fixed Language</InputLabel>
                    <Select
                      labelId="upload-fixed-language-label"
                      value={coerceFixedLanguageCodeForEngine(form.engine, form.languageCode)}
                      label="Fixed Language"
                      onChange={(event) => {
                        setForm({ ...form, languageCode: event.target.value });
                        setError('');
                      }}
                    >
                      {languageOptions.map((option) => (
                        <MenuItem key={option.code} value={option.code}>
                          {option.label} ({option.code})
                        </MenuItem>
                      ))}
                    </Select>
                  </FormControl>
                )}

                <FormControlLabel
                  control={(
                    <Switch
                      checked={form.audioNormalizationEnabled}
                      onChange={(event) => {
                        setForm({ ...form, audioNormalizationEnabled: event.target.checked });
                        setError('');
                      }}
                    />
                  )}
                  label="Audio Normalization"
                />

                <FormControlLabel
                  control={(
                    <Switch
                      checked={form.diarizationEnabled}
                      onChange={(event) => {
                        setForm({
                          ...form,
                          diarizationEnabled: event.target.checked,
                          speakerRoleAttributionEnabled: event.target.checked ? form.speakerRoleAttributionEnabled : false,
                        });
                        setError('');
                      }}
                    />
                  )}
                  label="Speaker Diarization"
                />

                {form.diarizationEnabled && (
                  <FormControl size="small" sx={{ minWidth: 200 }}>
                    <InputLabel id="upload-diarization-source-label">Diarization Source</InputLabel>
                    <Select
                      labelId="upload-diarization-source-label"
                      label="Diarization Source"
                      value={form.diarizationSource}
                      onChange={(event) => setForm({
                        ...form,
                        diarizationSource: event.target.value === 'Provider' || event.target.value === 'Xai'
                          ? event.target.value
                          : 'Local',
                      })}
                    >
                      <MenuItem value="Local">Local mode</MenuItem>
                      {providerDiarizationSupported && <MenuItem value="Provider">Provider mode</MenuItem>}
                      {xaiDiarizationSupported && <MenuItem value="Xai">xAI mode</MenuItem>}
                    </Select>
                  </FormControl>
                )}

                {form.diarizationEnabled && form.diarizationSource === 'Provider' && (
                  <Typography variant="caption" color="text.secondary">
                    Speaker detection is performed by {formatEngineLabel(form.engine)}. Local Basic/Improved processing is skipped.
                  </Typography>
                )}

                {form.diarizationEnabled && form.diarizationSource === 'Xai' && (
                  <Typography variant="caption" color="text.secondary">
                    {XAI_DIARIZATION_DISCLOSURE}
                  </Typography>
                )}

                {form.diarizationEnabled && form.diarizationSource === 'Local' && (
                  <FormControl size="small" sx={{ minWidth: 200 }}>
                    <InputLabel id="upload-diarization-mode-label">Local Diarization Mode</InputLabel>
                    <Select
                      labelId="upload-diarization-mode-label"
                      label="Local Diarization Mode"
                      value={form.diarizationMode}
                      onChange={(event) => setForm({ ...form, diarizationMode: event.target.value })}
                    >
                      {DIARIZATION_MODES.map((option) => (
                        <MenuItem key={option.value} value={option.value}>
                          {option.label}
                        </MenuItem>
                      ))}
                    </Select>
                  </FormControl>
                )}

                <FormControlLabel
                  control={(
                    <Switch
                      checked={form.speakerRoleAttributionEnabled}
                      disabled={!form.diarizationEnabled || !transcriptionOptions?.speakerRoleAttributionAvailable}
                      onChange={(event) => setForm({ ...form, speakerRoleAttributionEnabled: event.target.checked })}
                      inputProps={{ 'aria-describedby': 'upload-role-help' }}
                    />
                  )}
                  label="Identify professor and students"
                />
                <Typography id="upload-role-help" variant="caption" color="text.secondary">
                  {!form.diarizationEnabled
                    ? 'Enable Speaker Diarization first.'
                    : !transcriptionOptions?.speakerRoleAttributionAvailable
                      ? 'Configure an OpenRouter API key on the server to enable role attribution.'
                      : 'Uses timestamped speaker turns only; audio is not sent for role attribution.'}
                </Typography>
                {form.speakerRoleAttributionEnabled && (
                  <Alert severity="warning">
                    Timestamped transcript text is sent through OpenRouter to {transcriptionOptions?.speakerRoleAttributionModel} and incurs an additional charge. Audio is not sent for this step.
                  </Alert>
                )}

                <FormControlLabel
                  control={(
                    <Switch
                      checked={autoQueue}
                      onChange={(event) => setAutoQueue(event.target.checked)}
                    />
                  )}
                  label="Queue immediately after upload"
                />
              </Box>
            </>
          )}
        </Box>

        <Divider />

        <Box
          sx={{
            position: 'sticky',
            bottom: 0,
            px: { xs: 2, sm: 3 },
            py: 2,
            pl: { xs: 'calc(16px + var(--safe-area-left))', sm: 3 },
            pr: { xs: 'calc(16px + var(--safe-area-right))', sm: 3 },
            pb: { xs: 'calc(16px + var(--safe-area-bottom))', sm: 2 },
            display: 'flex',
            flexDirection: { xs: 'column-reverse', sm: 'row' },
            justifyContent: 'flex-end',
            gap: 1.5,
            bgcolor: 'background.paper',
          }}
        >
          <Button variant="text" onClick={onClose} disabled={saving}>
            Cancel
          </Button>
          <Button
            variant="contained"
            onClick={handleSubmit}
            disabled={saving || settingsLoading || optionsLoading || !form || files.length === 0}
          >
            {autoQueue ? 'Create and Queue' : 'Create Projects'}
          </Button>
        </Box>
      </Box>
    </Drawer>
  );
}
