#!/usr/bin/env bash
set -euo pipefail

readonly REPOSITORY_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
readonly BASE_REF='68c02796807e24d3b9108cf0e10b7ad217b706f7'
readonly HYPHEN='-'
readonly UNDERSCORE='_'
readonly CLOUDFLARE_TERM='cloudflare''d'
readonly SECURE_MCP_TERM='secure mcp ''tunnel'
readonly HOME_USER_PREFIX='/home''/user/'
readonly PRIVATE_WORKTREE_PREFIX="${HOME_USER_PREFIX}Documents/transcript${HYPHEN}lab${HYPHEN}nova${HYPHEN}private${HYPHEN}mcp"
readonly OBSOLETE_PATTERN="tunnel${HYPHEN}client|CONTROL${UNDERSCORE}PLANE${UNDERSCORE}API${UNDERSCORE}KEY|openai/tunnel${HYPHEN}client|${HYPHEN}${HYPHEN}tunnel${HYPHEN}id|TUNNEL${UNDERSCORE}PROFILE|transcriptlab${HYPHEN}health[.]url|${CLOUDFLARE_TERM}|${SECURE_MCP_TERM}"
readonly WORKSPACE_PATTERN="${HOME_USER_PREFIX}|${PRIVATE_WORKTREE_PREFIX}"
readonly PRIVATE_KEY_PATTERN='-----BEGIN( [A-Z0-9]+)? PRIVATE KEY-----'
readonly AWS_ACCESS_KEY_PATTERN='(AKIA|ASIA)[0-9A-Z]{16}|A3T[A-Z0-9]{17}'
readonly GITHUB_TOKEN_PATTERN='gh[pousr]_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,}'
readonly SK_SECRET_PATTERN="s${HYPHEN}k${HYPHEN}[A-Za-z0-9_-]{20,}"

TEMPORARY_DIRECTORY=''
declare -i FINDING_COUNT=0

fail() {
  printf 'FAIL: %s\n' "$*" >&2
  exit 1
}

cleanup() {
  [[ -z "${TEMPORARY_DIRECTORY}" ]] && return 0
  rm -rf -- "${TEMPORARY_DIRECTORY}"
  [[ ! -e "${TEMPORARY_DIRECTORY}" ]] || fail 'temporary guard fixtures were not removed'
  printf 'PASS: temporary guard fixtures removed\n'
}

trap cleanup EXIT INT TERM

record_if_matches_file() {
  local scope="$1"
  local file="$2"
  local label="$3"
  local pattern="$4"

  if LC_ALL=C grep -EIq -- "${pattern}" "${file}" 2>/dev/null; then
    printf 'FORBIDDEN: %s (%s)\n' "${label}" "${scope}" >&2
    FINDING_COUNT+=1
  fi
}

scan_file() {
  local scope="$1"
  local file="$2"

  record_if_matches_file "${scope}" "${file}" 'obsolete MCP client material' "${OBSOLETE_PATTERN}"
  record_if_matches_file "${scope}" "${file}" 'workspace path' "${WORKSPACE_PATTERN}"
  record_if_matches_file "${scope}" "${file}" 'private-key block' "${PRIVATE_KEY_PATTERN}"
  record_if_matches_file "${scope}" "${file}" 'AWS-style access key' "${AWS_ACCESS_KEY_PATTERN}"
  record_if_matches_file "${scope}" "${file}" 'GitHub token' "${GITHUB_TOKEN_PATTERN}"
  record_if_matches_file "${scope}" "${file}" 'sk-style secret' "${SK_SECRET_PATTERN}"
}

scan_tracked_content() {
  local path
  while IFS= read -r -d '' path; do
    scan_file "tracked:${path}" "${REPOSITORY_ROOT}/${path}"
  done < <(git -C "${REPOSITORY_ROOT}" ls-files -z)
}

scan_diff() {
  local scope="$1"
  shift
  local raw_diff_file="${TEMPORARY_DIRECTORY}/${scope}.raw.diff"
  local diff_file="${TEMPORARY_DIRECTORY}/${scope}.added.diff"

  "$@" >"${raw_diff_file}"
  awk '/^\+\+\+ / { next } /^\+/ { print }' "${raw_diff_file}" >"${diff_file}"
  record_if_matches_file "diff:${scope}" "${diff_file}" 'obsolete MCP client material' "${OBSOLETE_PATTERN}"
  record_if_matches_file "diff:${scope}" "${diff_file}" 'workspace path' "${WORKSPACE_PATTERN}"
  record_if_matches_file "diff:${scope}" "${diff_file}" 'private-key block' "${PRIVATE_KEY_PATTERN}"
  record_if_matches_file "diff:${scope}" "${diff_file}" 'AWS-style access key' "${AWS_ACCESS_KEY_PATTERN}"
  record_if_matches_file "diff:${scope}" "${diff_file}" 'GitHub token' "${GITHUB_TOKEN_PATTERN}"
  record_if_matches_file "diff:${scope}" "${diff_file}" 'sk-style secret' "${SK_SECRET_PATTERN}"
}

verify_guard_fixtures() {
  local prohibited_fixture="${TEMPORARY_DIRECTORY}/prohibited.txt"
  local allowed_fixture="${TEMPORARY_DIRECTORY}/allowed.txt"
  local before_count

  printf '%s\n' "tunnel${HYPHEN}client" >"${prohibited_fixture}"
  before_count=${FINDING_COUNT}
  scan_file fixture-prohibited "${prohibited_fixture}"
  (( FINDING_COUNT > before_count )) || fail 'prohibited fixture did not trigger the guard'
  FINDING_COUNT=${before_count}

  printf '%s\n' 'example.com /data /path/to/... /Users/transcriptlab/... YOUR_API_KEY changeme release runtime redaction OpenVINO' >"${allowed_fixture}"
  scan_file fixture-allowed "${allowed_fixture}"
  (( FINDING_COUNT == before_count )) || fail 'allowed fixture triggered the guard'
  printf 'PASS: prohibited and allowed guard fixtures behave as expected\n'
}

main() {
  git -C "${REPOSITORY_ROOT}" rev-parse --verify --quiet "${BASE_REF}^{commit}" >/dev/null \
    || fail "required baseline ${BASE_REF} is unavailable"
  TEMPORARY_DIRECTORY="$(mktemp -d)"
  verify_guard_fixtures
  scan_tracked_content
  scan_diff base-to-head git -C "${REPOSITORY_ROOT}" diff --no-ext-diff --binary "${BASE_REF}...HEAD"
  scan_diff base-to-worktree git -C "${REPOSITORY_ROOT}" diff --no-ext-diff --binary "${BASE_REF}"

  (( FINDING_COUNT == 0 )) || fail "public repository guard found ${FINDING_COUNT} forbidden tracked-content or diff match(es)"
  printf 'PASS: tracked content and base-relative diffs contain no obsolete MCP material or credential/path leaks\n'
}

main "$@"
