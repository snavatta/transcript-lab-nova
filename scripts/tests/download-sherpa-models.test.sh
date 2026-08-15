#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
script="${repo_root}/scripts/download-sherpa-models.sh"
fixture_root="$(mktemp -d)"

cleanup() {
  rm -rf "${fixture_root}"
}
trap cleanup EXIT INT TERM

bash -n "${script}"

invalid_output="${fixture_root}/invalid-target.txt"
if bash "${script}" invalid >"${invalid_output}" 2>&1; then
  echo "expected an invalid target to fail" >&2
  exit 1
fi
grep -Fq 'Usage:' "${invalid_output}"

whisper_root="${fixture_root}/whisper"
sense_voice_root="${fixture_root}/sense-voice"

for model in small medium; do
  mkdir -p "${whisper_root}/${model}"
  : >"${whisper_root}/${model}/config.json"
done
: >"${whisper_root}/small/tiny-encoder.onnx"
: >"${whisper_root}/medium/base-encoder.onnx"

mkdir -p "${sense_voice_root}/small"
: >"${sense_voice_root}/small/config.json"
: >"${sense_voice_root}/small/model.int8.onnx"
: >"${sense_voice_root}/small/tokens.txt"

all_output="${fixture_root}/all.txt"
SHERPA_MODELS_PATH="${whisper_root}" \
SHERPA_SENSE_VOICE_MODELS_PATH="${sense_voice_root}" \
  bash "${script}" all >"${all_output}"

grep -Fq "Model 'small' already exists" "${all_output}"
grep -Fq "Model 'medium' already exists" "${all_output}"
grep -Fq 'SenseVoice model already exists' "${all_output}"
grep -Fxq 'Done.' "${all_output}"

echo 'PASS: Sherpa model downloader syntax and dispatch'
