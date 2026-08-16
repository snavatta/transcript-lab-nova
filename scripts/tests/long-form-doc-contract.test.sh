#!/usr/bin/env bash
set -euo pipefail

readonly repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
readonly contract_file='class-transcriber-shared-api-contract.md'
readonly frontend_prd_file='class-transcriber-frontend-prd.md'
readonly backend_prd_file='class-transcriber-backend-prd.md'
readonly frontend_stack_file='class-transcriber-frontend-tech-stack-requirements.md'
readonly backend_stack_file='class-transcriber-backend-tech-stack-requirements.md'
readonly design_file='DESIGN.md'
readonly readme_file='README.md'
readonly environment_file='.env.example'
readonly appsettings_file='src/ClassTranscriber.Api/appsettings.json'
readonly declared_capabilities_top_level_keys=$'architecture\ncollectedAtUtc\ncomputeBackends\nhardwareName\nhostedProviders\nlogicalProcessorCount\nosDescription'
readonly scoped_files=(
  "${contract_file}"
  "${frontend_prd_file}"
  "${backend_prd_file}"
  "${frontend_stack_file}"
  "${backend_stack_file}"
  "${design_file}"
  "${readme_file}"
  "${environment_file}"
  "${appsettings_file}"
)

declare -i failure_count=0

fail() {
  printf 'FAIL: %s\n' "$*" >&2
  failure_count+=1
}

require_text() {
  local relative_path="$1"
  local expected_text="$2"
  if grep -Fq -- "${expected_text}" "${repository_root}/${relative_path}"; then
    printf 'PASS: %s contains required contract clause\n' "${relative_path}"
  else
    fail "${relative_path} is missing required contract clause"
  fi
}

forbid_text() {
  local relative_path="$1"
  local prohibited_text="$2"
  if grep -Fq -- "${prohibited_text}" "${repository_root}/${relative_path}"; then
    fail "${relative_path} contains superseded or unsafe wording"
  else
    printf 'PASS: %s omits superseded or unsafe wording\n' "${relative_path}"
  fi
}

forbid_pattern() {
  local relative_path="$1"
  local prohibited_pattern="$2"
  if grep -Eiq -- "${prohibited_pattern}" "${repository_root}/${relative_path}"; then
    fail "${relative_path} contains a prohibited credential or private-address pattern"
  else
    printf 'PASS: %s omits prohibited credential and private-address patterns\n' "${relative_path}"
  fi
}

forbid_added_text() {
  local relative_path="$1"
  local prohibited_text="$2"
  if git -C "${repository_root}" diff --no-ext-diff -- "${relative_path}" \
    | awk '/^\+[^+]/ { print }' \
    | grep -Fq -- "${prohibited_text}"; then
    fail "${relative_path} adds superseded or unsafe wording"
  else
    printf 'PASS: %s adds no superseded or unsafe wording\n' "${relative_path}"
  fi
}

verify_documented_capabilities_shape() {
  local documented_keys
  if ! documented_keys=$(awk '
    $0 == "### GET `/api/settings/capabilities`" { in_section = 1; next }
    in_section && /^```json$/ { in_json = 1; next }
    in_json && /^```$/ { exit }
    in_json { print }
  ' "${repository_root}/${contract_file}" | jq -r 'keys_unsorted[]' | sort); then
    fail "${contract_file} capabilities example is not valid JSON"
    return
  fi

  if [[ "${documented_keys}" == "${declared_capabilities_top_level_keys}" ]]; then
    printf 'PASS: documented capabilities JSON matches the declared seven top-level fields\n'
  else
    fail "${contract_file} capabilities JSON does not match the declared seven top-level fields"
  fi
}

require_contract() {
  require_text "${contract_file}" 'GET `/api/settings/capabilities`'
  require_text "${contract_file}" '`diarizationSource: string` — `"Local"`, `"Provider"`, or `"Xai"`'
  require_text "${contract_file}" '`wordTimestampModels: string[]`'
  require_text "${contract_file}" '`openai/whisper-large-v3` and `openai/whisper-large-v3-turbo`'
  require_text "${contract_file}" 'strictly smaller than `24,000,000` bytes'
  require_text "${contract_file}" '`600`-second core intervals'
  require_text "${contract_file}" 'up to two seconds before and after'
  require_text "${contract_file}" 'one whole prepared FLAC request'
  require_text "${contract_file}" '500 MB'
  require_text "${contract_file}" 'explicit xAI diarization failure is fatal'
  require_text "${contract_file}" 'greatest positive temporal overlap'
  require_text "${contract_file}" 'nearest xAI interval within one second'
  require_text "${contract_file}" '`collectedAtUtc`'
  require_text "${contract_file}" '`hostedProviders`'
  require_text "${contract_file}" '`computeBackends`'
  require_text "${contract_file}" '`hardwareName`'
  require_text "${contract_file}" 'The exact top-level response fields are `collectedAtUtc`, `hostedProviders`, `computeBackends`, `architecture`, `logicalProcessorCount`, `osDescription`, and `hardwareName`.'
  verify_documented_capabilities_shape
  require_text "${contract_file}" 'integer micro-USD'
  require_text "${contract_file}" 'without double counting'
}

require_product_documents() {
  require_text "${frontend_prd_file}" '`Settings`, `Local Model Manager`, and `System Capabilities`'
  require_text "${frontend_prd_file}" 'lazy-load both the model catalog and system capabilities'
  require_text "${frontend_prd_file}" 'Diagnostics remains a separate route'
  require_text "${frontend_prd_file}" '`Xai` source only for the two verified OpenRouter word-timestamp models'
  require_text "${frontend_prd_file}" '/api/settings/capabilities'
  require_text "${backend_prd_file}" 'lossless FLAC for every hosted-provider upload'
  require_text "${backend_prd_file}" 'OpenRouter ordinary model discovery remains dynamic'
  require_text "${backend_prd_file}" 'successful OpenRouter chunk is checkpointed'
  require_text "${backend_prd_file}" 'xAI timing merge is fatal'
  require_text "${backend_prd_file}" 'Additive migrations preserve existing `Engine=Xai, DiarizationSource=Provider` rows'
  require_text "${backend_prd_file}" 'GET `/api/settings/capabilities`'
  require_text "${frontend_stack_file}" 'Settings tab content lazy-loads after first activation'
  require_text "${frontend_stack_file}" '`Xai` is available only when the capability/options contract permits it'
  require_text "${backend_stack_file}" 'whole lossless FLAC'
  require_text "${backend_stack_file}" 'OpenRouter word-timestamp long-form mode is limited to exactly'
  require_text "${backend_stack_file}" 'timeouts are fatal and are not retried'
  require_text "${design_file}" '`Settings`, `Local Model Manager`, and `System Capabilities`'
  require_text "${design_file}" 'Diagnostics remains outside those tabs'
  require_text "${readme_file}" 'All hosted transcription audio is prepared as lossless FLAC.'
  require_text "${readme_file}" 'Optional paid smoke tests are intentionally skipped'
  require_text "${environment_file}" 'Transcription__Xai__ApiKey=YOUR_XAI_API_KEY'
  require_text "${appsettings_file}" '"Xai"'
}

check_guardrails() {
  local relative_path
  for relative_path in "${scoped_files[@]}"; do
    forbid_text "${relative_path}" '/home/'
    if [[ "${relative_path}" == "${readme_file}" ]]; then
      forbid_added_text "${relative_path}" '/Users/'
    else
      forbid_text "${relative_path}" '/Users/'
    fi
    forbid_text "${relative_path}" 'private service'
    forbid_text "${relative_path}" 'paid smoke passed'
    forbid_text "${relative_path}" 'paid smoke completed'
    forbid_pattern "${relative_path}" '(AKIA|ASIA)[0-9A-Z]{16}|gh[pousr]_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,}|\bsk-[A-Za-z0-9_-]{20,}\b'
    forbid_pattern "${relative_path}" 'https?://(10\.[0-9]|192\.168\.|172\.(1[6-9]|2[0-9]|3[0-1])\.)|https?://[^/[:space:]]+\.(local|internal|lan)([:/]|$)'
  done
  forbid_text "${frontend_prd_file}" 'below the defaults form in a vertical stack layout'
  forbid_text "${readme_file}" 'prepared WAV is checked against xAI'
  forbid_text "${backend_prd_file}" 'may losslessly convert WAV to FLAC'

  local word_additions word_removals
  word_additions=$(scoped_word_diff | awk '/^\+[^+]/ { count += 1 } END { print count + 0 }')
  word_removals=$(scoped_word_diff | awk '/^-[^-]/ { count += 1 } END { print count + 0 }')
  printf 'GUARDRAIL: scoped word-diff additions=%s removals=%s\n' "${word_additions}" "${word_removals}"
}

scoped_word_diff() {
  git -C "${repository_root}" diff --word-diff=porcelain -- "${scoped_files[@]}"
  local relative_path status
  for relative_path in "${scoped_files[@]}"; do
    if git -C "${repository_root}" ls-files --error-unmatch -- "${relative_path}" >/dev/null 2>&1; then
      continue
    fi
    status=0
    git -C "${repository_root}" diff --no-index --word-diff=porcelain -- /dev/null "${relative_path}" || status=$?
    [[ "${status}" -eq 1 ]] || return "${status}"
  done
}

main() {
  [[ $# -le 1 ]] || { printf 'usage: %s [--guardrails]\n' "$0" >&2; exit 2; }
  require_contract
  require_product_documents
  if [[ "${1:-}" == '--guardrails' ]]; then
    check_guardrails
  fi
  (( failure_count == 0 )) || exit 1
  printf 'PASS: long-form documentation contract holds\n'
}

main "$@"
