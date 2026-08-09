#!/usr/bin/env bash
# Publish TranscriptLab Nova for native Apple Silicon / CoreML Whisper runs.
#
# Usage:
#   ./scripts/publish-macos-coreml.sh [--output <path>] [--configuration <name>]
#
# The script publishes both the API and the isolated WhisperNet worker for
# osx-arm64. At runtime, set Transcription__WhisperNet__WorkerPath to the
# self-contained worker executable copied into the publish directory.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

OUTPUT_PATH="${REPO_ROOT}/publish/transcriptlab-macos"
CONFIGURATION="Release"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --output)
      OUTPUT_PATH="$2"
      shift 2
      ;;
    --configuration)
      CONFIGURATION="$2"
      shift 2
      ;;
    *)
      echo "Unknown option: $1" >&2
      exit 1
      ;;
  esac
done

TEMP_ROOT="$(mktemp -d)"
WORKER_OUTPUT="${TEMP_ROOT}/whispernet-worker"

cleanup() {
  rm -rf "$TEMP_ROOT"
}
trap cleanup EXIT

echo "Publishing TranscriptLab Nova API for osx-arm64 ..."
dotnet publish "${REPO_ROOT}/src/ClassTranscriber.Api/ClassTranscriber.Api.csproj" \
  -c "$CONFIGURATION" \
  -r osx-arm64 \
  --self-contained true \
  -o "$OUTPUT_PATH"

echo "Publishing WhisperNet worker for osx-arm64 ..."
dotnet publish "${REPO_ROOT}/src/ClassTranscriber.WhisperNet.Worker/ClassTranscriber.WhisperNet.Worker.csproj" \
  -c "$CONFIGURATION" \
  -r osx-arm64 \
  --self-contained true \
  -o "$WORKER_OUTPUT"

echo "Copying self-contained WhisperNet worker into API publish directory ..."
cp -R "${WORKER_OUTPUT}/." "$OUTPUT_PATH/"

echo ""
echo "Publish complete: $OUTPUT_PATH"
echo ""
echo "Recommended native macOS runtime environment:"
echo "  export ASPNETCORE_ENVIRONMENT=Production"
echo "  export Storage__BasePath=/Users/transcriptlab/transcriptlab/data"
echo "  export Transcription__FFmpegPath=/opt/homebrew/bin/ffmpeg"
echo "  export Transcription__WhisperNet__WorkerPath=${OUTPUT_PATH}/ClassTranscriber.WhisperNet.Worker"
echo ""
echo "Run:"
echo "  ${OUTPUT_PATH}/ClassTranscriber.Api"
