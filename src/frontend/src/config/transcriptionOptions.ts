const ENGINE_MODEL_OPTIONS = {
  SherpaOnnx: ['small', 'medium'],
  SherpaOnnxSenseVoice: ['small'],
  WhisperNet: ['tiny', 'base', 'small', 'medium', 'large', 'large-v3-turbo'],
  WhisperNetCuda: ['tiny', 'base', 'small', 'medium', 'large', 'large-v3-turbo'],
  OpenVinoWhisperSidecar: ['tiny-int8', 'tiny-fp16', 'base-int8', 'base-fp16', 'small-int8', 'small-fp16', 'medium-int8', 'medium-fp16', 'large-v3-int8', 'large-v3-fp16'],
  OpenAiCompatible: [] as string[],
} as const;

export const TRANSCRIPTION_ENGINES = Object.keys(ENGINE_MODEL_OPTIONS) as Array<keyof typeof ENGINE_MODEL_OPTIONS>;

export function getModelsForEngine(engine: string): readonly string[] {
  return ENGINE_MODEL_OPTIONS[engine as keyof typeof ENGINE_MODEL_OPTIONS] ?? ENGINE_MODEL_OPTIONS.WhisperNet;
}

export function normalizeModelForEngine(engine: string, model: string): string {
  const models = getModelsForEngine(engine);
  return models.includes(model) ? model : models[0];
}
