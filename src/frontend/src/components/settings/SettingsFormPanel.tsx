import type { Dispatch, SetStateAction } from 'react';
import {
  Box,
  FormControl,
  InputLabel,
  MenuItem,
  Paper,
  Select,
  Typography,
} from '@mui/material';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import TuneIcon from '@mui/icons-material/Tune';
import type { TranscriptionOptionsDto, UpdateGlobalSettingsRequest } from '../../types';
import { formatEngineLabel } from '../../utils/transcription';
import { coerceFixedLanguageCodeForEngine, getLanguageOptionsForEngine } from '../../utils/languages';
import { getDefaultDiarizationSource } from '../../config/diarizationOptions';
import { SettingsProcessingOptions } from './SettingsProcessingOptions';

interface SettingsFormPanelProps {
  readonly form: UpdateGlobalSettingsRequest;
  readonly setForm: Dispatch<SetStateAction<UpdateGlobalSettingsRequest | null>>;
  readonly transcriptionOptions: TranscriptionOptionsDto | undefined;
}

export function SettingsFormPanel({ form, setForm, transcriptionOptions }: SettingsFormPanelProps) {
  const engineOptions = transcriptionOptions?.engines
    ?? [{ engine: form.defaultEngine, models: [form.defaultModel], providerDiarizationModels: [], wordTimestampModels: [] }];
  const modelOptions = engineOptions.find((option) => option.engine === form.defaultEngine)?.models
    ?? [form.defaultModel];
  const languageOptions = getLanguageOptionsForEngine(form.defaultEngine);
  const handleEngineChange = (engine: string) => {
    const models = transcriptionOptions?.engines.find((option) => option.engine === engine)?.models
      ?? [form.defaultModel];
    setForm((current) => {
      if (!current) return current;
      const model = models.includes(current.defaultModel) ? current.defaultModel : (models[0] ?? current.defaultModel);
      return {
        ...current,
        defaultEngine: engine,
        defaultModel: model,
        defaultDiarizationSource: getDefaultDiarizationSource(transcriptionOptions, engine, model),
        defaultLanguageCode: current.defaultLanguageMode === 'Fixed'
          ? coerceFixedLanguageCodeForEngine(engine, current.defaultLanguageCode)
          : null,
      };
    });
  };

  const handleLanguageModeChange = (languageMode: 'Auto' | 'Fixed') => {
    setForm((current) => current && {
      ...current,
      defaultLanguageMode: languageMode,
      defaultLanguageCode: languageMode === 'Fixed'
        ? coerceFixedLanguageCodeForEngine(current.defaultEngine, current.defaultLanguageCode)
        : null,
    });
  };

  return (
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
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(2, minmax(0, 1fr))' }, gap: 2 }}>
          <FormControl fullWidth>
            <InputLabel id="default-engine-label">Engine</InputLabel>
            <Select
              labelId="default-engine-label"
              value={form.defaultEngine}
              label="Engine"
              onChange={(event) => handleEngineChange(event.target.value)}
            >
              {engineOptions.map((option) => (
                <MenuItem key={option.engine} value={option.engine}>{formatEngineLabel(option.engine)}</MenuItem>
              ))}
            </Select>
          </FormControl>

          <FormControl fullWidth>
            <InputLabel id="default-model-label">Model</InputLabel>
            <Select
              labelId="default-model-label"
              value={form.defaultModel}
              label="Model"
              onChange={(event) => {
                const model = event.target.value;
                setForm({
                  ...form,
                  defaultModel: model,
                  defaultDiarizationSource: getDefaultDiarizationSource(
                    transcriptionOptions,
                    form.defaultEngine,
                    model,
                  ),
                });
              }}
            >
              {modelOptions.map((model) => (
                <MenuItem key={model} value={model}>{model}</MenuItem>
              ))}
            </Select>
          </FormControl>
        </Box>

        {(form.defaultEngine === 'OpenVinoWhisperSidecar'
          || form.defaultEngine === 'OpenAiCompatible'
          || form.defaultEngine === 'OpenRouter'
          || form.defaultEngine === 'Xai') && (
          <Box sx={{ display: 'flex', alignItems: 'flex-start', gap: 1, p: 1.5, borderRadius: 1, bgcolor: 'action.hover' }}>
            <InfoOutlinedIcon sx={{ fontSize: 16, mt: 0.25, color: 'text.secondary', flexShrink: 0 }} />
            <Typography variant="caption" color="text.secondary">
              {form.defaultEngine === 'OpenVinoWhisperSidecar'
                ? 'Uses a local OpenVINO GPU sidecar. Requires the OpenVINO Python environment to be configured.'
                : form.defaultEngine === 'Xai'
                  ? 'Recommended for long classes. The entire prepared audio file is sent directly to xAI in one request.'
                  : form.defaultEngine === 'OpenRouter'
                    ? "Uses OpenRouter's hosted speech-to-text API. Requires an OpenRouter API key configured on the server. Audio is sent to the selected remote transcription provider."
                    : 'Requires backend configuration in appsettings.json. Contact your administrator to configure the target URL and model.'}
            </Typography>
          </Box>
        )}

        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(2, minmax(0, 1fr))' }, gap: 2 }}>
          <FormControl fullWidth>
            <InputLabel id="default-language-mode-label">Language Mode</InputLabel>
            <Select
              labelId="default-language-mode-label"
              value={form.defaultLanguageMode}
              label="Language Mode"
              onChange={(event) => handleLanguageModeChange(event.target.value === 'Fixed' ? 'Fixed' : 'Auto')}
            >
              <MenuItem value="Auto">Auto-detect</MenuItem>
              <MenuItem value="Fixed">Fixed</MenuItem>
            </Select>
          </FormControl>

          {form.defaultLanguageMode === 'Fixed' && (
            <FormControl fullWidth>
              <InputLabel id="default-fixed-language-label">Fixed Language</InputLabel>
              <Select
                labelId="default-fixed-language-label"
                value={coerceFixedLanguageCodeForEngine(form.defaultEngine, form.defaultLanguageCode)}
                label="Fixed Language"
                onChange={(event) => setForm({ ...form, defaultLanguageCode: event.target.value })}
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

        <SettingsProcessingOptions
          form={form}
          setForm={setForm}
          transcriptionOptions={transcriptionOptions}
        />
      </Box>
    </Paper>
  );
}
