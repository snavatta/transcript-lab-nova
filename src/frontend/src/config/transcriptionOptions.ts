const ENGINE_MODEL_OPTIONS = {
  Whisper: ['tiny', 'base', 'small', 'medium', 'large'],
  SherpaOnnx: ['small', 'medium'],
} as const;

export const TRANSCRIPTION_ENGINES = Object.keys(ENGINE_MODEL_OPTIONS) as Array<keyof typeof ENGINE_MODEL_OPTIONS>;

export function getModelsForEngine(engine: string): readonly string[] {
  return ENGINE_MODEL_OPTIONS[engine as keyof typeof ENGINE_MODEL_OPTIONS] ?? ENGINE_MODEL_OPTIONS.Whisper;
}

export function normalizeModelForEngine(engine: string, model: string): string {
  const models = getModelsForEngine(engine);
  return models.includes(model) ? model : models[0];
}