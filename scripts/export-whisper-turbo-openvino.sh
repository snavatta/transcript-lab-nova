#!/usr/bin/env bash
# Export openai/whisper-large-v3-turbo to OpenVINO INT8 (or FP16) format
# using Optimum-Intel, producing a model compatible with openvino-genai 2026+.
#
# The model is exported WITHOUT --disable-stateful so that the decoder includes
# the beam_idx port required by the openvino-genai WhisperPipeline at inference.
#
# Usage:
#   ./scripts/export-whisper-turbo-openvino.sh [options]
#
# Options:
#   --venv      <path>   Path to the Python venv created by setup-openvino-export-env.sh.
#                        Defaults to ~/ov-export-env.
#   --output    <path>   Directory to write the exported model into.
#                        Defaults to ./whisper-large-v3-turbo-int8-ov
#   --format    <fmt>    Weight format: int8 (default) or fp16.
#   --model     <id>     HuggingFace model ID to export.
#                        Defaults to openai/whisper-large-v3-turbo.
#   --help               Show this help and exit.
#
# Examples:
#   # Export INT8 (default, ~1 GB output)
#   ./scripts/export-whisper-turbo-openvino.sh --venv ~/ov-export-env
#
#   # Export FP16
#   ./scripts/export-whisper-turbo-openvino.sh --venv ~/ov-export-env --format fp16 --output ./whisper-large-v3-turbo-fp16-ov
#
#   # Custom output path
#   ./scripts/export-whisper-turbo-openvino.sh --output /data/models/whisper-large-v3-turbo-int8-ov
#
# After the export finishes, copy the output directory to the homelab:
#   scp -r ./whisper-large-v3-turbo-int8-ov homelab:/path/to/data/models/openvino-genai/large-v3-turbo-int8

set -euo pipefail

# ---------------------------------------------------------------------------
# Defaults
# ---------------------------------------------------------------------------

VENV_PATH="${HOME}/ov-export-env"
OUTPUT_DIR="./whisper-large-v3-turbo-int8-ov"
WEIGHT_FORMAT="int8"
MODEL_ID="openai/whisper-large-v3-turbo"

# ---------------------------------------------------------------------------
# Argument parsing
# ---------------------------------------------------------------------------

while [[ $# -gt 0 ]]; do
  case "$1" in
    --venv)
      VENV_PATH="$2"
      shift 2
      ;;
    --output)
      OUTPUT_DIR="$2"
      shift 2
      ;;
    --format)
      WEIGHT_FORMAT="$2"
      shift 2
      ;;
    --model)
      MODEL_ID="$2"
      shift 2
      ;;
    --help|-h)
      sed -n '2,/^set -/p' "$0" | grep '^#' | sed 's/^# \?//'
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      exit 1
      ;;
  esac
done

# Validate weight format
if [[ "$WEIGHT_FORMAT" != "int8" && "$WEIGHT_FORMAT" != "fp16" ]]; then
  echo "Error: --format must be 'int8' or 'fp16', got '${WEIGHT_FORMAT}'" >&2
  exit 1
fi

# ---------------------------------------------------------------------------
# Activate venv
# ---------------------------------------------------------------------------

if [[ ! -f "${VENV_PATH}/bin/activate" ]]; then
  echo "Error: virtual environment not found at ${VENV_PATH}" >&2
  echo "Run ./scripts/setup-openvino-export-env.sh first." >&2
  exit 1
fi

# shellcheck disable=SC1091
source "${VENV_PATH}/bin/activate"

# ---------------------------------------------------------------------------
# Verify optimum-cli is available
# ---------------------------------------------------------------------------

if ! command -v optimum-cli &>/dev/null; then
  echo "Error: optimum-cli not found in PATH after activating ${VENV_PATH}" >&2
  echo "Run ./scripts/setup-openvino-export-env.sh to install dependencies." >&2
  exit 1
fi

OPTIMUM_VERSION=$(python -c "import optimum; print(optimum.__version__)" 2>/dev/null || echo "unknown")
echo "optimum version  : ${OPTIMUM_VERSION}"
echo "Model            : ${MODEL_ID}"
echo "Weight format    : ${WEIGHT_FORMAT}"
echo "Output directory : ${OUTPUT_DIR}"
echo ""

# ---------------------------------------------------------------------------
# Export
#
# Key flag: --task automatic-speech-recognition
# Intentionally NO --disable-stateful — the stateful decoder emits beam_idx
# which is required by openvino-genai 2026+ WhisperPipeline.
# ---------------------------------------------------------------------------

echo "Starting export (this will take 20-45 minutes on a modern CPU) ..."
echo ""

optimum-cli export openvino \
  --model "${MODEL_ID}" \
  --weight-format "${WEIGHT_FORMAT}" \
  --task automatic-speech-recognition \
  "${OUTPUT_DIR}"

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

echo ""
echo "Export complete."
echo ""
echo "Output directory: ${OUTPUT_DIR}"
echo ""
du -sh "${OUTPUT_DIR}" 2>/dev/null && echo ""
echo "Copy this directory to your homelab, e.g.:"
echo "  scp -r ${OUTPUT_DIR} homelab:/path/to/data/models/openvino-genai/large-v3-turbo-${WEIGHT_FORMAT}"
echo ""
echo "Then re-add the model to the catalog in:"
echo "  src/ClassTranscriber.Api/Transcription/OpenVinoGenAiTranscriptionEngine.cs"
echo "  src/ClassTranscriber.Api/Tools/openvino_whisper_sidecar.py"
echo "  src/frontend/src/config/transcriptionOptions.ts"
