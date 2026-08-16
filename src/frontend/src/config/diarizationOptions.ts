import type { DiarizationSource, TranscriptionOptionsDto } from '../types';

export type DiarizationMode = 'Basic' | 'Improved';

const XAI_DIARIZATION_OPENROUTER_MODELS = [
  'openai/whisper-large-v3',
  'openai/whisper-large-v3-turbo',
] as const;

export const XAI_DIARIZATION_DISCLOSURE = 'OpenRouter transcribes the audio first. xAI receives the whole prepared FLAC for timing-based speaker labels. If xAI diarization fails, the job fails; it does not fall back to local or provider diarization.';

export const DIARIZATION_MODES: { value: DiarizationMode; label: string; description: string }[] = [
  {
    value: 'Basic',
    label: 'Basic',
    description: 'Pitch-based clustering, up to 3 speakers',
  },
  {
    value: 'Improved',
    label: 'Improved',
    description: 'Richer spectral analysis, up to 6 speakers',
  },
];

export function supportsProviderDiarization(
  options: TranscriptionOptionsDto | undefined,
  engine: string,
  model: string,
): boolean {
  return options?.engines
    .find((option) => option.engine === engine)
    ?.providerDiarizationModels?.includes(model) ?? false;
}

export function supportsXaiDiarization(
  options: TranscriptionOptionsDto | undefined,
  engine: string,
  model: string,
): boolean {
  const engineOption = options?.engines.find((option) => option.engine === engine);
  return options?.xaiDiarizationAvailable === true
    && engine === 'OpenRouter'
    && (XAI_DIARIZATION_OPENROUTER_MODELS as readonly string[]).includes(model)
    && engineOption?.wordTimestampModels?.includes(model) === true
    && engineOption.providerDiarizationModels?.includes(model) === false;
}

export function getDefaultDiarizationSource(
  options: TranscriptionOptionsDto | undefined,
  engine: string,
  model: string,
): DiarizationSource {
  return supportsProviderDiarization(options, engine, model) ? 'Provider' : 'Local';
}

export function coerceDiarizationSource(
  options: TranscriptionOptionsDto | undefined,
  source: string | undefined,
  engine: string,
  model: string,
): DiarizationSource {
  if (source === 'Provider' && supportsProviderDiarization(options, engine, model)) {
    return 'Provider';
  }

  if (source === 'Xai' && supportsXaiDiarization(options, engine, model)) {
    return 'Xai';
  }

  return 'Local';
}
