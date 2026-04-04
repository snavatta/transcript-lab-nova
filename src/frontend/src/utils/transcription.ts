export function formatEngineLabel(engine: string): string {
  switch (engine) {
    case 'SherpaOnnxSenseVoice':
      return 'SherpaOnnx.Sense.Voice';
    case 'WhisperNet':
      return 'WhisperNet.CPU';
    case 'WhisperNetCuda':
      return 'WhisperNet.CUDA';
    case 'OpenVinoGenAi':
      return 'OpenVINO.GenAI';
    case 'OpenVinoWhisperSidecar':
      return 'OpenVINO Sidecar';
    case 'OnnxWhisper':
      return 'ONNX Whisper (coming soon)';
    case 'OpenAiCompatible':
      return 'OpenAI-Compatible API';
    default:
      return engine;
  }
}
