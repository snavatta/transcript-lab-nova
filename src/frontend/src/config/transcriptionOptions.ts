const ENGINE_MODEL_OPTIONS = {
  SherpaOnnx: ['small', 'medium'],
  SherpaOnnxSenseVoice: ['small'],
  WhisperNet: ['tiny', 'base', 'small', 'medium', 'large'],
  WhisperNetCuda: ['tiny', 'base', 'small', 'medium', 'large'],
  WhisperNetOpenVino: ['tiny', 'base', 'small', 'medium', 'large'],
  OpenVinoGenAi: ['base-int8', 'small-fp16', 'tiny-int8'],
} as const;

export const TRANSCRIPTION_ENGINES = Object.keys(ENGINE_MODEL_OPTIONS) as Array<keyof typeof ENGINE_MODEL_OPTIONS>;

export function getModelsForEngine(engine: string): readonly string[] {
  return ENGINE_MODEL_OPTIONS[engine as keyof typeof ENGINE_MODEL_OPTIONS] ?? ENGINE_MODEL_OPTIONS.WhisperNet;
}

export function normalizeModelForEngine(engine: string, model: string): string {
  const models = getModelsForEngine(engine);
  return models.includes(model) ? model : models[0];
}
