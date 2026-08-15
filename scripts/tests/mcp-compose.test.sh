#!/usr/bin/env bash
set -euo pipefail

REPOSITORY_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
readonly REPOSITORY_ROOT
readonly OVERLAY_FILE="${REPOSITORY_ROOT}/docker-compose.mcp.yml"
readonly ENV_FILE="${REPOSITORY_ROOT}/.env.example"
readonly SECRET_NAME='transcriptlab-mcp-cursor-integrity-key'
readonly SECRET_TARGET='/run/secrets/transcriptlab-mcp-cursor-integrity-key'
readonly EXPECTED_URLS='http://+:5000;http://+:5001'
readonly OBSOLETE_PATTERN='tunnel''-client|control[_-]?plane|openai/''tunnel''-client|--tunnel''-id|tunnel''_profile|transcriptlab-health''[.]url|cloud''flared|secure mcp ''tunnel'
readonly FORBIDDEN_CREDENTIAL_KEY='CONTROL'"_PLANE"'_API_KEY'

TEST_DIRECTORY=''

fail() {
  printf 'FAIL: %s\n' "$*" >&2
  exit 1
}

cleanup() {
  [[ -z "${TEST_DIRECTORY}" ]] && return 0
  rm -rf -- "${TEST_DIRECTORY}"
  [[ ! -e "${TEST_DIRECTORY}" ]] || fail 'temporary Compose fixtures were not removed'
  printf 'PASS: temporary Compose fixtures removed\n'
}

trap cleanup EXIT INT TERM

require_tools() {
  command -v docker >/dev/null || fail 'docker is required'
  command -v jq >/dev/null || fail 'jq is required'
  command -v timeout >/dev/null || fail 'timeout is required'
  timeout 15 docker compose version >/dev/null || fail 'docker compose is required'
}

secret_path_from_env_file() {
  local value
  value="$(awk -F= '$1 == "MCP_CURSOR_KEY_FILE" { print substr($0, index($0, "=") + 1); exit }' "${ENV_FILE}")"
  [[ -n "${value}" ]] || fail '.env.example does not define MCP_CURSOR_KEY_FILE'
  printf '%s\n' "${value}"
}

render() {
  local env_file="$1"
  local base_file="$2"
  local overlay_file="$3"
  local output_file="$4"

  timeout 30 docker compose --env-file "${env_file}" \
    -f "${base_file}" -f "${overlay_file}" config --format json >"${output_file}"
}

render_base() {
  local env_file="$1"
  local base_file="$2"
  local output_file="$3"

  timeout 30 docker compose --env-file "${env_file}" \
    -f "${base_file}" config --format json >"${output_file}"
}

compose_semantics_hold() {
  local base_json="$1"
  local merged_json="$2"
  local label="$3"
  local expected_secret_path
  expected_secret_path="$(secret_path_from_env_file)"

  jq -e \
    --arg secret_name "${SECRET_NAME}" \
    --arg secret_target "${SECRET_TARGET}" \
    --arg expected_secret_path "${expected_secret_path}" \
    --arg expected_urls "${EXPECTED_URLS}" \
    --arg obsolete_pattern "${OBSOLETE_PATTERN}" \
    --argjson expected_env_keys '["ASPNETCORE_URLS", "Mcp__CursorIntegrityKeyFile", "Mcp__Enabled", "Mcp__PrivatePort"]' \
    '
      . as $merged
      | input as $base
      | ($merged.services | keys) as $service_names
      | ($merged.services.transcriptlab) as $service
      | ($base.services.transcriptlab.ports // []) as $base_ports
      | ($merged.services.transcriptlab.ports // []) as $merged_ports
      | ($base.services.transcriptlab.environment // {} | keys | sort) as $base_env_keys
      | ($service.environment // {} | keys | sort) as $merged_env_keys
      | ($merged.secrets // {}) as $top_level_secrets
      | ($service.secrets // []) as $secret_mounts
      | ($merged | del(.services.transcriptlab.ports, .services.transcriptlab.environment, .services.transcriptlab.secrets, .secrets)) as $merged_without_allowed_overlay_fields
      | ($base | del(.services.transcriptlab.ports, .services.transcriptlab.environment, .services.transcriptlab.secrets, .secrets)) as $base_without_allowed_overlay_fields
      | [
          ($service_names == ["transcriptlab"]),
          ($merged_without_allowed_overlay_fields == $base_without_allowed_overlay_fields),
          (($merged_ports | map(select((.target | tostring) == "5000"))) == ($base_ports | map(select((.target | tostring) == "5000")))),
          ($merged_ports | length == (($base_ports | length) + 1)),
          (($merged_ports | map(select((.target | tostring) == "5001"))) == [{"mode":"ingress","host_ip":"127.0.0.1","target":5001,"published":"5001","protocol":"tcp"}]),
          ($service.environment.ASPNETCORE_URLS == $expected_urls),
          ($service.environment.Mcp__Enabled == "true"),
          ($service.environment.Mcp__PrivatePort == "5001"),
          ($service.environment.Mcp__CursorIntegrityKeyFile == $secret_target),
          ($merged_env_keys == (($base_env_keys + $expected_env_keys) | unique | sort)),
          ($top_level_secrets | keys == [$secret_name]),
          ($top_level_secrets[$secret_name].file == $expected_secret_path),
          ($top_level_secrets[$secret_name].file | type == "string" and test("^/")),
          ($secret_mounts | length == 1),
          ($secret_mounts[0].source == $secret_name),
          ($secret_mounts[0].target == $secret_target),
          (($secret_mounts[0].mode | tostring) == "292" or ($secret_mounts[0].mode | tostring) == "0444"),
          ([ $merged | .. | strings | ascii_downcase | select(test($obsolete_pattern)) ] | length == 0)
        ]
      | all
    ' "${merged_json}" "${base_json}" >/dev/null
}

assert_overlay_semantics() {
  local base_json="$1"
  local merged_json="$2"
  local label="$3"

  compose_semantics_hold "${base_json}" "${merged_json}" "${label}" \
    || fail "${label} Compose render violates the private MCP overlay contract"
}

assert_semantic_failure() {
  local name="$1"
  local base_file="$2"
  local overlay_file="$3"
  local env_file="$4"
  local base_json="${TEST_DIRECTORY}/${name}.base.json"
  local merged_json="${TEST_DIRECTORY}/${name}.merged.json"
  local failure_log="${TEST_DIRECTORY}/${name}.failure.log"

  render_base "${env_file}" "${base_file}" "${base_json}"
  render "${env_file}" "${base_file}" "${overlay_file}" "${merged_json}"
  if compose_semantics_hold "${base_json}" "${merged_json}" "${name}" >"${failure_log}" 2>&1; then
    fail "${name} mutation unexpectedly satisfied the semantic checker"
  fi
}

write_mutation_fixture() {
  local name="$1"
  local mutation_file="${TEST_DIRECTORY}/${name}.yml"

  cp -- "${OVERLAY_FILE}" "${mutation_file}"
  case "${name}" in
    bind-host)
      sed -i 's/127[.]0[.]0[.]1:5001:5001/0.0.0.0:5001:5001/' "${mutation_file}"
      ;;
    secret-target)
      sed -i 's#/run/secrets/transcriptlab-mcp-cursor-integrity-key#/run/secrets/wrong-target#' "${mutation_file}"
      ;;
    added-service)
      sed -i '/^secrets:/i\  unintended:\n    image: busybox:latest\n' "${mutation_file}"
      ;;
    forbidden-credential)
      sed -i "/^    environment:/a\\      ${FORBIDDEN_CREDENTIAL_KEY}: should-not-render" "${mutation_file}"
      ;;
    *)
      fail "unknown mutation fixture: ${name}"
      ;;
  esac
  printf '%s\n' "${mutation_file}"
}

verify_valid_renders() {
  local normal_base="${TEST_DIRECTORY}/normal.base.json"
  local normal_merged="${TEST_DIRECTORY}/normal.merged.json"
  local casaos_base="${TEST_DIRECTORY}/casaos.base.json"
  local casaos_merged="${TEST_DIRECTORY}/casaos.merged.json"

  render_base "${ENV_FILE}" "${REPOSITORY_ROOT}/docker-compose.yml" "${normal_base}"
  render "${ENV_FILE}" "${REPOSITORY_ROOT}/docker-compose.yml" "${OVERLAY_FILE}" "${normal_merged}"
  assert_overlay_semantics "${normal_base}" "${normal_merged}" normal

  render_base "${ENV_FILE}" "${REPOSITORY_ROOT}/docker-compose.casaos.yml" "${casaos_base}"
  render "${ENV_FILE}" "${REPOSITORY_ROOT}/docker-compose.casaos.yml" "${OVERLAY_FILE}" "${casaos_merged}"
  assert_overlay_semantics "${casaos_base}" "${casaos_merged}" casaos

  printf 'PASS: normal and CasaOS Compose renders satisfy private MCP semantics\n'
}

verify_invalid_secret_inputs() {
  local unset_env="${TEST_DIRECTORY}/unset.env"
  local relative_env="${TEST_DIRECTORY}/relative.env"
  local base_json="${TEST_DIRECTORY}/invalid.base.json"
  local merged_json="${TEST_DIRECTORY}/invalid.merged.json"

  : >"${unset_env}"
  printf 'MCP_CURSOR_KEY_FILE=relative/cursor-integrity-key\n' >"${relative_env}"

  if render "${unset_env}" "${REPOSITORY_ROOT}/docker-compose.yml" "${OVERLAY_FILE}" "${merged_json}" 2>/dev/null; then
    render_base "${unset_env}" "${REPOSITORY_ROOT}/docker-compose.yml" "${base_json}"
    if compose_semantics_hold "${base_json}" "${merged_json}" unset-secret-path >/dev/null 2>&1; then
      fail 'unset secret path unexpectedly satisfied the semantic checker'
    fi
  fi

  assert_semantic_failure invalid-secret-path "${REPOSITORY_ROOT}/docker-compose.yml" "${OVERLAY_FILE}" "${relative_env}"
  printf 'PASS: unset or relative secret paths cannot satisfy the semantic contract\n'
}

verify_mutation_fixtures() {
  local name
  local fixture
  for name in bind-host secret-target added-service forbidden-credential; do
    fixture="$(write_mutation_fixture "${name}")"
    assert_semantic_failure "${name}" "${REPOSITORY_ROOT}/docker-compose.yml" "${fixture}" "${ENV_FILE}"
  done
  printf 'PASS: all Compose mutation fixtures fail semantic validation\n'
}

main() {
  require_tools
  [[ -f "${OVERLAY_FILE}" ]] || fail 'docker-compose.mcp.yml is required'
  TEST_DIRECTORY="$(mktemp -d)"
  verify_valid_renders
  verify_invalid_secret_inputs
  verify_mutation_fixtures
}

main "$@"
