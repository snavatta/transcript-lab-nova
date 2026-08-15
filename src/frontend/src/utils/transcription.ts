export function formatEngineLabel(engine: string): string {
  switch (engine) {
    case 'SherpaOnnxSenseVoice':
      return 'SherpaOnnx.Sense.Voice';
    case 'WhisperNet':
      return 'WhisperNet.CPU';
    case 'WhisperNetCuda':
      return 'WhisperNet.CUDA';
    case 'WhisperNetCoreML':
      return 'WhisperNet.CoreML';
    case 'OpenVinoWhisperSidecar':
      return 'OpenVINO Sidecar';
    case 'OnnxWhisper':
      return 'ONNX Whisper (coming soon)';
    case 'OpenAiCompatible':
      return 'OpenAI-Compatible API';
    case 'OpenRouter':
      return 'OpenRouter';
    default:
      return engine;
  }
}
