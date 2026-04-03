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
import type { LanguageMode, ProjectSettingsDto } from '../../types';
import { formatEngineLabel } from '../../utils/transcription';
import { coerceFixedLanguageCodeForEngine, getLanguageOptionsForEngine } from '../../utils/languages';
import { useNotification } from '../notifications';

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
  },
  engineOptions: Array<{ engine: string; models: string[] }>,
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

    return [{ engine: settings.defaultEngine, models: [settings.defaultModel] }];
  }, [settings, transcriptionOptions]);

  useEffect(() => {
    if (!open) {
      return;
    }

    setItems(createItems(files));
    setAutoQueue(true);
    setError('');

    if (settings && engineOptions.length > 0) {
      setForm(createDefaultSettings(settings, engineOptions));
    }
  }, [open, files, settings, engineOptions]);

  const modelOptions = form
    ? (engineOptions.find((option) => option.engine === form.engine)?.models ?? [form.model])
    : [];
  const languageOptions = form ? getLanguageOptionsForEngine(form.engine) : [];

  const handleItemNameChange = (index: number, projectName: string) => {
    setItems((current) => current.map((item, itemIndex) => (
      itemIndex === index ? { ...item, projectName } : item
    )));
  };

  const handleEngineChange = (engine: string) => {
    const models = engineOptions.find((option) => option.engine === engine)?.models ?? [];
    setForm((current) => current
      ? {
          ...current,
          engine,
          model: models.includes(current.model) ? current.model : (models[0] ?? current.model),
          languageCode: current.languageMode === 'Fixed'
            ? coerceFixedLanguageCodeForEngine(engine, current.languageCode)
            : null,
        }
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
      PaperProps={{ sx: { width: { xs: '100%', sm: 560 } } }}
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
          <Typography variant="h6">Review Batch Upload</Typography>
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
                  <InputLabel>Engine</InputLabel>
                  <Select
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

                <FormControl fullWidth>
                  <InputLabel>Model</InputLabel>
                  <Select
                    value={form.model}
                    label="Model"
                    onChange={(event) => {
                      setForm({ ...form, model: event.target.value });
                      setError('');
                    }}
                  >
                    {modelOptions.map((model) => (
                      <MenuItem key={model} value={model}>{model}</MenuItem>
                    ))}
                  </Select>
                </FormControl>

                <FormControl fullWidth>
                  <InputLabel>Language Mode</InputLabel>
                  <Select
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
                    <InputLabel>Fixed Language</InputLabel>
                    <Select
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
                        setForm({ ...form, diarizationEnabled: event.target.checked });
                        setError('');
                      }}
                    />
                  )}
                  label="Speaker Diarization"
                />

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
