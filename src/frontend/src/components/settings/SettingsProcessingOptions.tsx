import type { Dispatch, SetStateAction } from 'react';
import {
  Alert,
  Box,
  Divider,
  FormControl,
  InputLabel,
  MenuItem,
  Select,
  Switch,
  Typography,
} from '@mui/material';
import type { TranscriptionOptionsDto, UpdateGlobalSettingsRequest } from '../../types';
import { formatEngineLabel } from '../../utils/transcription';
import {
  DIARIZATION_MODES,
  supportsProviderDiarization,
  supportsXaiDiarization,
  XAI_DIARIZATION_DISCLOSURE,
} from '../../config/diarizationOptions';

interface SettingsProcessingOptionsProps {
  readonly form: UpdateGlobalSettingsRequest;
  readonly setForm: Dispatch<SetStateAction<UpdateGlobalSettingsRequest | null>>;
  readonly transcriptionOptions: TranscriptionOptionsDto | undefined;
}

export function SettingsProcessingOptions({
  form,
  setForm,
  transcriptionOptions,
}: SettingsProcessingOptionsProps) {
  const providerDiarizationSupported = supportsProviderDiarization(
    transcriptionOptions,
    form.defaultEngine,
    form.defaultModel,
  );
  const xaiDiarizationSupported = supportsXaiDiarization(
    transcriptionOptions,
    form.defaultEngine,
    form.defaultModel,
  );

  return (
    <>
      <Divider />
      <Typography variant="subtitle2" color="text.secondary">Processing Options</Typography>

      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 2 }}>
        <Box>
          <Typography variant="body2" fontWeight={500}>Audio Normalization</Typography>
          <Typography variant="caption" color="text.secondary">
            Levels audio volume before transcription for improved accuracy on quiet or variable recordings.
          </Typography>
        </Box>
        <Switch
          checked={form.defaultAudioNormalizationEnabled}
          onChange={(event) => setForm({ ...form, defaultAudioNormalizationEnabled: event.target.checked })}
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
          onChange={(event) => setForm({
            ...form,
            defaultDiarizationEnabled: event.target.checked,
            defaultSpeakerRoleAttributionEnabled: event.target.checked
              ? form.defaultSpeakerRoleAttributionEnabled
              : false,
          })}
          inputProps={{ 'aria-label': 'Speaker Diarization' }}
        />
      </Box>

      {form.defaultDiarizationEnabled && (
        <FormControl fullWidth>
          <InputLabel id="default-diarization-source-label">Diarization Source</InputLabel>
          <Select
            labelId="default-diarization-source-label"
            value={form.defaultDiarizationSource}
            label="Diarization Source"
            onChange={(event) => setForm({
              ...form,
              defaultDiarizationSource: event.target.value === 'Provider' || event.target.value === 'Xai'
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

      {form.defaultDiarizationEnabled && form.defaultDiarizationSource === 'Provider' && (
        <Typography variant="caption" color="text.secondary">
          Speaker detection is performed by {formatEngineLabel(form.defaultEngine)}. Local Basic/Improved processing is skipped.
        </Typography>
      )}

      {form.defaultDiarizationEnabled && form.defaultDiarizationSource === 'Xai' && (
        <Typography variant="caption" color="text.secondary">
          {XAI_DIARIZATION_DISCLOSURE}
        </Typography>
      )}

      {form.defaultDiarizationEnabled && form.defaultDiarizationSource === 'Local' && (
        <FormControl fullWidth>
          <InputLabel id="default-diarization-mode-label">Local Diarization Mode</InputLabel>
          <Select
            labelId="default-diarization-mode-label"
            value={form.defaultDiarizationMode}
            label="Local Diarization Mode"
            onChange={(event) => setForm({ ...form, defaultDiarizationMode: event.target.value })}
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

      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 2 }}>
        <Box>
          <Typography variant="body2" fontWeight={500}>Speaker Role Attribution</Typography>
          <Typography id="settings-role-help" variant="caption" color="text.secondary">
            {!form.defaultDiarizationEnabled
              ? 'Enable Speaker Diarization first. '
              : !transcriptionOptions?.speakerRoleAttributionAvailable
                ? 'Configure an OpenRouter API key on the server to enable this option. '
                : ''}
            Sends timestamped speaker turns, not audio, through OpenRouter to {transcriptionOptions?.speakerRoleAttributionModel ?? 'Gemini'}.
          </Typography>
        </Box>
        <Switch
          checked={form.defaultSpeakerRoleAttributionEnabled}
          disabled={!form.defaultDiarizationEnabled || !transcriptionOptions?.speakerRoleAttributionAvailable}
          onChange={(event) => setForm({ ...form, defaultSpeakerRoleAttributionEnabled: event.target.checked })}
          inputProps={{ 'aria-label': 'Speaker Role Attribution', 'aria-describedby': 'settings-role-help' }}
        />
      </Box>

      {!transcriptionOptions?.speakerRoleAttributionAvailable && (
        <Alert severity="info">Speaker-role attribution is unavailable until an OpenRouter API key is configured on the server.</Alert>
      )}

      <Divider />
      <Typography variant="subtitle2" color="text.secondary">Display Options</Typography>

      <FormControl fullWidth>
        <InputLabel id="default-transcript-view-label">Default Transcript View</InputLabel>
        <Select
          labelId="default-transcript-view-label"
          value={form.defaultTranscriptViewMode}
          label="Default Transcript View"
          onChange={(event) => setForm({
            ...form,
            defaultTranscriptViewMode: event.target.value === 'Timestamped' ? 'Timestamped' : 'Readable',
          })}
        >
          <MenuItem value="Readable">Readable</MenuItem>
          <MenuItem value="Timestamped">Timestamped</MenuItem>
        </Select>
      </FormControl>
    </>
  );
}
