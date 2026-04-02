#!/usr/bin/env bash
# Download SherpaOnnx models for TranscriptLab Nova.
#
# Usage:
#   ./scripts/download-sherpa-models.sh [small|medium|sense-voice|all]
#
# Models are placed under the configured models path
# (default: /data/models/sherpa-onnx/<size>/ and /data/models/sherpa-onnx-sense-voice/<size>/).

set -euo pipefail

MODELS_BASE="${SHERPA_MODELS_PATH:-/data/models/sherpa-onnx}"
SENSE_VOICE_MODELS_BASE="${SHERPA_SENSE_VOICE_MODELS_PATH:-/data/models/sherpa-onnx-sense-voice}"
RELEASE_BASE="https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models"

# Map our registered model names to upstream sherpa-onnx whisper tarballs and file prefixes.
declare -A MODEL_TARBALLS=(
  [small]="sherpa-onnx-whisper-tiny.tar.bz2"
  [medium]="sherpa-onnx-whisper-base.tar.bz2"
)
declare -A MODEL_PREFIXES=(
  [small]="tiny"
  [medium]="base"
)

download_model() {
  local name="$1"
  local tarball="${MODEL_TARBALLS[$name]}"
  local prefix="${MODEL_PREFIXES[$name]}"
  local dest="${MODELS_BASE}/${name}"

  if [[ -f "${dest}/config.json" ]] && [[ -f "${dest}/${prefix}-encoder.onnx" ]]; then
    echo "Model '${name}' already exists at ${dest}, skipping."
    return
  fi

  echo "Downloading ${tarball} for model '${name}'..."
  local tmp
  tmp="$(mktemp -d)"

  curl -fSL "${RELEASE_BASE}/${tarball}" -o "${tmp}/${tarball}"
  echo "Extracting..."
  tar -xjf "${tmp}/${tarball}" -C "${tmp}"

  mkdir -p "${dest}"

  # The tarball extracts to a directory named like 'sherpa-onnx-whisper-tiny'.
  local extracted_dir
  extracted_dir="$(find "${tmp}" -maxdepth 1 -mindepth 1 -type d | head -1)"

  # Copy the required model files.
  cp "${extracted_dir}/${prefix}-encoder.onnx" "${dest}/"
  cp "${extracted_dir}/${prefix}-decoder.onnx" "${dest}/"
  cp "${extracted_dir}/${prefix}-tokens.txt" "${dest}/"

  # Rewrite config.json so rerunning the script repairs any legacy mapping.
  cat > "${dest}/config.json" <<EOF
{
  "backend": "whisper",
  "encoder": "${prefix}-encoder.onnx",
  "decoder": "${prefix}-decoder.onnx",
  "tokens": "${prefix}-tokens.txt",
  "task": "transcribe"
}

download_sense_voice_model() {
  local dest="${SENSE_VOICE_MODELS_BASE}/small"
  local tarball="sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17.tar.bz2"

  if [[ -f "${dest}/config.json" ]] && [[ -f "${dest}/model.int8.onnx" ]] && [[ -f "${dest}/tokens.txt" ]]; then
    echo "SenseVoice model already exists at ${dest}, skipping."
    return
  fi

  echo "Downloading ${tarball} for SenseVoice..."
  local tmp
  tmp="$(mktemp -d)"

  curl -fSL "${RELEASE_BASE}/${tarball}" -o "${tmp}/${tarball}"
  echo "Extracting..."
  tar -xjf "${tmp}/${tarball}" -C "${tmp}"

  mkdir -p "${dest}"

  local extracted_dir
  extracted_dir="$(find "${tmp}" -maxdepth 1 -mindepth 1 -type d | head -1)"

  cp "${extracted_dir}/model.int8.onnx" "${dest}/"
  cp "${extracted_dir}/tokens.txt" "${dest}/"

  cat > "${dest}/config.json" <<EOF
{
  "backend": "sense_voice",
  "model": "model.int8.onnx",
  "tokens": "tokens.txt",
  "use_itn": true
}
EOF

  rm -rf "${tmp}"
  echo "SenseVoice model installed to ${dest}."
}
EOF

  rm -rf "${tmp}"
  echo "Model '${name}' installed to ${dest}."
}

target="${1:-all}"

case "${target}" in
  small)
    download_model small
    ;;
  medium)
    download_model medium
    ;;
  sense-voice|sensevoice)
    download_sense_voice_model
    ;;
  all)
    download_model small
    download_model medium
    download_sense_voice_model
    ;;
  *)
    echo "Usage: $0 [small|medium|sense-voice|all]" >&2
    exit 1
    ;;
esac

echo "Done."
