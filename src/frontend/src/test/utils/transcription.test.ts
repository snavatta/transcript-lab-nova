import { describe, expect, it } from 'vitest';
import { getModelsForEngine } from '../../config/transcriptionOptions';
import { formatEngineLabel } from '../../utils/transcription';

describe('formatEngineLabel', () => {
  it('formats WhisperNetCoreML', () => {
    expect(formatEngineLabel('WhisperNetCoreML')).toBe('WhisperNet.CoreML');
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
});
