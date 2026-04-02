import { useEffect, useState } from 'react';
import {
  Box, Button, FormControl, InputLabel, Select, MenuItem,
  Switch, FormControlLabel, TextField, Paper, Typography, Skeleton, Divider,
} from '@mui/material';
import SaveIcon from '@mui/icons-material/Save';
import RestoreIcon from '@mui/icons-material/Restore';
import TopBar from '../components/shell/TopBar';
import { useSettings, useTranscriptionOptions } from '../hooks/useData';
import { settingsApi } from '../api';
import { useNotification } from '../components/notifications';
import { useSWRConfig } from 'swr';
import type { UpdateGlobalSettingsRequest, TranscriptViewMode, LanguageMode } from '../types';

export default function SettingsPage() {
  const { data: settings, isLoading } = useSettings();
  const { data: transcriptionOptions } = useTranscriptionOptions();
  const { notify } = useNotification();
  const { mutate } = useSWRConfig();
  const [form, setForm] = useState<UpdateGlobalSettingsRequest | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (settings && !form) {
      setForm({
        defaultEngine: settings.defaultEngine,
        defaultModel: settings.defaultModel,
        defaultLanguageMode: settings.defaultLanguageMode,
        defaultLanguageCode: settings.defaultLanguageCode,
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
        defaultLanguageCode: settings.defaultLanguageCode,
        defaultAudioNormalizationEnabled: settings.defaultAudioNormalizationEnabled,
        defaultDiarizationEnabled: settings.defaultDiarizationEnabled,
        defaultTranscriptViewMode: settings.defaultTranscriptViewMode,
      });
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

      <Paper variant="outlined" sx={{ p: 3, maxWidth: 600 }}>
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
                <MenuItem key={option.engine} value={option.engine}>{option.engine}</MenuItem>
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
              onChange={(e) => setForm({ ...form, defaultLanguageMode: e.target.value as LanguageMode })}
            >
              <MenuItem value="Auto">Auto-detect</MenuItem>
              <MenuItem value="Fixed">Fixed</MenuItem>
            </Select>
          </FormControl>

          {form.defaultLanguageMode === 'Fixed' && (
            <TextField
              label="Language Code"
              value={form.defaultLanguageCode ?? ''}
              onChange={(e) => setForm({ ...form, defaultLanguageCode: e.target.value || null })}
              placeholder="e.g., en, es, fr"
              helperText="ISO 639-1 language code"
              fullWidth
            />
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
    </>
  );
}
