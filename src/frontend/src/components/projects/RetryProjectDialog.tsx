import { useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  FormControl,
  FormControlLabel,
  InputLabel,
  MenuItem,
  Select,
  Switch,
  Typography,
} from '@mui/material';
import { useSWRConfig } from 'swr';
import { ApiError, projectsApi } from '../../api';
import { useProject, useTranscriptionOptions } from '../../hooks/useData';
import type { LanguageMode, ProjectDetailDto, ProjectSettingsDto } from '../../types';
import { formatEngineLabel } from '../../utils/transcription';
import { coerceFixedLanguageCodeForEngine, getLanguageOptionsForEngine } from '../../utils/languages';
import { useNotification } from '../notifications';
import { DIARIZATION_MODES } from '../../config/diarizationOptions';

interface Props {
  open: boolean;
  projectId: string;
  project?: ProjectDetailDto;
  onClose: () => void;
}

function normalizeLanguageMode(languageMode: string): LanguageMode {
  return languageMode === 'Fixed' ? 'Fixed' : 'Auto';
}

function coerceSettings(
  settings: ProjectSettingsDto,
  engineOptions: Array<{ engine: string; models: string[] }>,
): ProjectSettingsDto {
  const fallbackEngine = engineOptions[0]?.engine ?? settings.engine;
  const engine = engineOptions.some((option) => option.engine === settings.engine)
    ? settings.engine
    : fallbackEngine;
  const models = engineOptions.find((option) => option.engine === engine)?.models ?? [settings.model];
  const model = models.includes(settings.model) ? settings.model : (models[0] ?? settings.model);
  const languageMode = normalizeLanguageMode(settings.languageMode);

  return {
    engine,
    model,
    languageMode,
    languageCode: languageMode === 'Fixed'
      ? coerceFixedLanguageCodeForEngine(engine, settings.languageCode)
      : null,
    audioNormalizationEnabled: settings.audioNormalizationEnabled,
    diarizationEnabled: settings.diarizationEnabled,
    diarizationMode: settings.diarizationMode ?? 'Basic',
  };
}

export default function RetryProjectDialog({
  open,
  projectId,
  project,
  onClose,
}: Props) {
  const { data: hydratedProject, isLoading: projectLoading } = useProject(open && !project ? projectId : undefined);
  const { data: transcriptionOptions, isLoading: optionsLoading } = useTranscriptionOptions();
  const resolvedProject = project ?? hydratedProject;
  const { mutate } = useSWRConfig();
  const { notify } = useNotification();
  const [form, setForm] = useState<ProjectSettingsDto | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const engineOptions = useMemo(() => {
    if (transcriptionOptions?.engines?.length) {
      return transcriptionOptions.engines;
    }

    return resolvedProject
      ? [{ engine: resolvedProject.settings.engine, models: [resolvedProject.settings.model] }]
      : [];
  }, [resolvedProject, transcriptionOptions]);

  useEffect(() => {
    if (!open || !resolvedProject || engineOptions.length === 0) {
      return;
    }

    setForm(coerceSettings(resolvedProject.settings, engineOptions));
    setError('');
  }, [open, resolvedProject, engineOptions]);

  const modelOptions = form
    ? (engineOptions.find((option) => option.engine === form.engine)?.models ?? [form.model])
    : [];
  const languageOptions = form ? getLanguageOptionsForEngine(form.engine) : [];

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

    setSaving(true);
    setError('');
    try {
      await projectsApi.retry(projectId, { settings: form });
      await mutate(`projects/${projectId}`);
      await mutate((key: unknown) => typeof key === 'string' && key.startsWith('projects?'), undefined);
      await mutate('queue');
      if (resolvedProject?.folderId) {
        await mutate(`folders/${resolvedProject.folderId}`);
      }
      notify('Project re-queued');
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to retry project');
    } finally {
      setSaving(false);
    }
  };

  return (
    <Dialog open={open} onClose={saving ? undefined : onClose} maxWidth="sm" fullWidth>
      <DialogTitle>Retry Project</DialogTitle>
      <DialogContent>
        {resolvedProject && (
          <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
            Choose the transcription settings to use for the new retry attempt. Available engines reflect the current runtime.
          </Typography>
        )}

        {error && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {error}
          </Alert>
        )}

        {(projectLoading || optionsLoading || !form) ? (
          <Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>
            Loading retry settings...
          </Typography>
        ) : (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2.5, mt: 2 }}>
            <FormControl fullWidth>
              <InputLabel>Engine</InputLabel>
              <Select
                value={form.engine}
                label="Engine"
                onChange={(e) => handleEngineChange(e.target.value)}
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
                onChange={(e) => {
                  setForm({ ...form, model: e.target.value });
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
                onChange={(e) => handleLanguageModeChange(e.target.value as LanguageMode)}
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
                  onChange={(e) => {
                    setForm({ ...form, languageCode: e.target.value });
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

            <Divider />

            <FormControlLabel
              control={(
                <Switch
                  checked={form.audioNormalizationEnabled}
                  onChange={(e) => {
                    setForm({ ...form, audioNormalizationEnabled: e.target.checked });
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
                  onChange={(e) => {
                    setForm({ ...form, diarizationEnabled: e.target.checked });
                    setError('');
                  }}
                />
              )}
              label="Speaker Diarization"
            />

            {form.diarizationEnabled && (
              <FormControl size="small" sx={{ minWidth: 200 }}>
                <InputLabel id="diarization-mode-label">Diarization Mode</InputLabel>
                <Select
                  labelId="diarization-mode-label"
                  label="Diarization Mode"
                  value={form.diarizationMode}
                  onChange={(e) => setForm({ ...form, diarizationMode: e.target.value })}
                >
                  {DIARIZATION_MODES.map((option) => (
                    <MenuItem key={option.value} value={option.value}>
                      {option.label}
                    </MenuItem>
                  ))}
                </Select>
              </FormControl>
            )}
          </Box>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={saving}>Cancel</Button>
        <Button
          onClick={handleSubmit}
          variant="contained"
          disabled={saving || !form || engineOptions.length === 0}
        >
          Retry
        </Button>
      </DialogActions>
    </Dialog>
  );
}
