export function formatEngineLabel(engine: string): string {
  switch (engine) {
    case 'SherpaOnnxSenseVoice':
      return 'SherpaOnnx.Sense.Voice';
    case 'WhisperNet':
      return 'WhisperNet.CPU';
    case 'WhisperNetCuda':
      return 'WhisperNet.CUDA';
    case 'WhisperNetOpenVino':
      return 'WhisperNet.OpenVINO';
    case 'OpenVinoGenAi':
      return 'OpenVINO.GenAI';
    default:
      return engine;
  }
}
