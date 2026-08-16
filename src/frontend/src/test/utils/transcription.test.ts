import { describe, expect, it } from 'vitest';
import { getModelsForEngine } from '../../config/transcriptionOptions';
import {
  coerceDiarizationSource,
  getDefaultDiarizationSource,
  supportsProviderDiarization,
} from '../../config/diarizationOptions';
import type { TranscriptionOptionsDto } from '../../types';
import { formatEngineLabel } from '../../utils/transcription';

describe('formatEngineLabel', () => {
  it('formats WhisperNetCoreML', () => {
    expect(formatEngineLabel('WhisperNetCoreML')).toBe('WhisperNet.CoreML');
  });

  it('formats OpenRouter', () => {
    expect(formatEngineLabel('OpenRouter')).toBe('OpenRouter');
  });

  it('formats direct xAI distinctly', () => {
    expect(formatEngineLabel('Xai')).toBe('xAI (direct)');
  });
});

describe('getModelsForEngine', () => {
  it('returns WhisperNet models for WhisperNetCoreML', () => {
    expect(getModelsForEngine('WhisperNetCoreML')).toEqual([
      'tiny',
      'base',
      'small',
      'medium',
      'large',
      'large-v3-turbo',
    ]);
  });

  it('keeps OpenRouter models data-driven', () => {
    expect(getModelsForEngine('OpenRouter')).toEqual([]);
  });
});

const transcriptionOptions: TranscriptionOptionsDto = {
  engines: [
    { engine: 'WhisperNet', models: ['small'], providerDiarizationModels: [], wordTimestampModels: [] },
    {
      engine: 'OpenRouter',
      models: ['openai/whisper-large-v3', 'deepgram/nova-3'],
      providerDiarizationModels: [],
      wordTimestampModels: ['openai/whisper-large-v3'],
    },
    {
      engine: 'Xai',
      models: ['grok-stt-1.0'],
      providerDiarizationModels: ['grok-stt-1.0'],
      wordTimestampModels: ['grok-stt-1.0'],
    },
  ],
  xaiDiarizationAvailable: true,
  xaiDiarizationModel: 'grok-stt-1.0',
  speakerRoleAttributionAvailable: true,
  speakerRoleAttributionModel: 'google/gemini-3.7-flash',
  recommendedHostedEngine: 'Xai',
  recommendedHostedModel: 'grok-stt-1.0',
};

describe('provider diarization capabilities', () => {
  it('enables Provider only for an advertised engine and model', () => {
    expect(supportsProviderDiarization(transcriptionOptions, 'Xai', 'grok-stt-1.0')).toBe(true);
    expect(supportsProviderDiarization(transcriptionOptions, 'WhisperNet', 'small')).toBe(false);
  });

  it('defaults supported models to Provider and other models to Local', () => {
    expect(getDefaultDiarizationSource(transcriptionOptions, 'Xai', 'grok-stt-1.0')).toBe('Provider');
    expect(getDefaultDiarizationSource(transcriptionOptions, 'WhisperNet', 'small')).toBe('Local');
  });

  it('preserves Xai only for an advertised OpenRouter word model', () => {
    expect(coerceDiarizationSource(
      transcriptionOptions,
      'Xai',
      'OpenRouter',
      'openai/whisper-large-v3',
    )).toBe('Xai');
    expect(coerceDiarizationSource(
      transcriptionOptions,
      'Xai',
      'OpenRouter',
      'deepgram/nova-3',
    )).toBe('Local');
  });

  it('coerces stale or malformed sources to Local', () => {
    expect(coerceDiarizationSource(
      transcriptionOptions,
      'Bogus',
      'OpenRouter',
      'openai/whisper-large-v3',
    )).toBe('Local');
    expect(coerceDiarizationSource(
      { ...transcriptionOptions, xaiDiarizationAvailable: false },
      'Xai',
      'OpenRouter',
      'openai/whisper-large-v3',
    )).toBe('Local');
  });
});
