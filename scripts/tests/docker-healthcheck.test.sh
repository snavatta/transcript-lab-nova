#!/usr/bin/env bash
set -euo pipefail

readonly REPOSITORY_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
readonly HEALTH_COMMAND='curl -fsS http://127.0.0.1:5000/api/health >/dev/null || exit 1'
readonly OBSOLETE_TUNNEL_TERM='tunnel''-client'
readonly OBSOLETE_IMAGE_TERM='openai/''tunnel'

fail() {
  printf 'FAIL: %s\n' "$*" >&2
  exit 1
}

require_runtime_curl() {
  local file="$1"

  awk '
    /^FROM .* AS runtime$/ { runtime_stage = 1 }
    runtime_stage && /(^|[[:space:]\\])curl([[:space:]\\]|$)/ { found = 1 }
    END { exit !found }
  ' "${REPOSITORY_ROOT}/${file}" || fail "${file} runtime stage does not install curl"
}

verify_default_topology() {
  local compose_output
  compose_output="$(docker compose -f "${REPOSITORY_ROOT}/docker-compose.yml" config)"

  grep -Fq '  transcriptlab:' <<<"${compose_output}" \
    || fail 'default Compose does not contain transcriptlab'
  if grep -Eqi "${OBSOLETE_TUNNEL_TERM}|control-plane|cursor.*key|(^|[[:space:]])secrets:" <<<"${compose_output}"; then
    fail 'default Compose includes tunnel or credential configuration'
  fi
}

verify_single_app_entrypoint() {
  local file
  for file in Dockerfile Dockerfile.cuda Dockerfile.openvino; do
    [[ "$(grep -Ec '^ENTRYPOINT \["dotnet", "ClassTranscriber.Api.dll"\]$' "${REPOSITORY_ROOT}/${file}")" == '1' ]] \
      || fail "${file} does not retain exactly one application entrypoint"
    if grep -Eqi "${OBSOLETE_TUNNEL_TERM}|${OBSOLETE_IMAGE_TERM}" "${REPOSITORY_ROOT}/${file}"; then
      fail "${file} contains a tunnel process or binary"
    fi
  done
}

verify_healthcheck_metadata() {
  local compose_output
  compose_output="$(docker compose -f "${REPOSITORY_ROOT}/docker-compose.yml" config)"

  grep -Fq 'healthcheck:' <<<"${compose_output}" || fail 'base Compose has no healthcheck'
  grep -Fq '    - CMD-SHELL' <<<"${compose_output}" || fail 'healthcheck does not use CMD-SHELL'
  grep -Fq "    - ${HEALTH_COMMAND}" <<<"${compose_output}" || fail 'healthcheck command is not exact'
  grep -Fq 'interval: 10s' <<<"${compose_output}" || fail 'healthcheck interval is not 10s'
  grep -Fq 'timeout: 3s' <<<"${compose_output}" || fail 'healthcheck timeout is not 3s'
  grep -Fq 'retries: 12' <<<"${compose_output}" || fail 'healthcheck retries are not 12'
  grep -Fq 'start_period: 30s' <<<"${compose_output}" || fail 'healthcheck start period is not 30s'
}

verify_renders() {
  docker compose -f "${REPOSITORY_ROOT}/docker-compose.yml" config >/dev/null
  docker compose -f "${REPOSITORY_ROOT}/docker-compose.yml" -f "${REPOSITORY_ROOT}/docker-compose.cuda.yml" config >/dev/null
  docker compose -f "${REPOSITORY_ROOT}/docker-compose.yml" -f "${REPOSITORY_ROOT}/docker-compose.openvino.yml" config >/dev/null
}

verify_static() {
  verify_default_topology
  verify_single_app_entrypoint
  require_runtime_curl Dockerfile
  require_runtime_curl Dockerfile.cuda
  require_runtime_curl Dockerfile.openvino
  verify_healthcheck_metadata
  verify_renders
}

readonly BUILD_TIMEOUT_SECONDS="${TASK6_BUILD_TIMEOUT_SECONDS:-1800}"
readonly HEALTH_TIMEOUT_SECONDS=153
declare -a BUILT_IMAGES=()
TEST_DIRECTORY=''

cleanup() {
  local image

  for image in "${BUILT_IMAGES[@]}"; do
    timeout 60 docker image rm -f "${image}" >/dev/null 2>&1 || true
  done
  [[ -z "${TEST_DIRECTORY}" ]] || rm -rf -- "${TEST_DIRECTORY}"
}

trap cleanup EXIT

build_and_inspect_image() {
  local name="$1"
  local dockerfile="$2"
  local image="transcriptlab-task6-${name}-$$"

  timeout "${BUILD_TIMEOUT_SECONDS}" docker build --quiet --tag "${image}" \
    --file "${REPOSITORY_ROOT}/${dockerfile}" "${REPOSITORY_ROOT}"
  BUILT_IMAGES+=("${image}")
  [[ "$(docker image inspect --format '{{json .Config.Entrypoint}}' "${image}")" == '["dotnet","ClassTranscriber.Api.dll"]' ]] \
    || fail "${name} image does not retain the application entrypoint"
  timeout 60 docker run --rm --entrypoint /bin/sh "${image}" -c 'command -v curl >/dev/null' \
    || fail "${name} image does not contain curl"
  printf 'PASS: %s image has curl and one application entrypoint\n' "${name}"
}

build_and_inspect_all_images() {
  [[ "${BUILD_TIMEOUT_SECONDS}" =~ ^[1-9][0-9]*$ ]] \
    || fail 'TASK6_BUILD_TIMEOUT_SECONDS must be a positive integer'
  build_and_inspect_image cpu Dockerfile
  build_and_inspect_image cuda Dockerfile.cuda
  build_and_inspect_image openvino Dockerfile.openvino
}

write_cpu_override() {
  local path="$1"
  local health_command="$2"
  local interval="$3"
  local timeout="$4"
  local retries="$5"
  local start_period="$6"

  cat >"${path}" <<EOF
services:
  transcriptlab:
    image: \${TASK6_IMAGE:?set TASK6_IMAGE}
    ports: !reset []
    healthcheck:
      test: ["CMD-SHELL", "${health_command}"]
      interval: ${interval}
      timeout: ${timeout}
      retries: ${retries}
      start_period: ${start_period}
EOF
}

wait_for_health_status() {
  local container_id="$1"
  local expected_status="$2"
  local deadline=$((SECONDS + HEALTH_TIMEOUT_SECONDS))
  local actual_status=''

  while (( SECONDS < deadline )); do
    actual_status="$(timeout 15 docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}missing{{end}}' "${container_id}")"
    if [[ "${actual_status}" == "${expected_status}" ]]; then
      timeout 15 docker inspect --format '{{json .State.Health}}' "${container_id}"
      return 0
    fi
    sleep 2
  done

  timeout 15 docker inspect --format '{{json .State.Health}}' "${container_id}" >&2 || true
  fail "${container_id} did not become ${expected_status} within ${HEALTH_TIMEOUT_SECONDS}s (last=${actual_status})"
}

run_cpu_health_scenario() {
  local scenario="$1"
  local health_command="$2"
  local interval="$3"
  local timeout="$4"
  local retries="$5"
  local start_period="$6"
  local expected_status="$7"
  local project="task6-health-${scenario}-$$"
  local override="${TEST_DIRECTORY}/${scenario}.yml"
  local container_id

  write_cpu_override "${override}" "${health_command}" "${interval}" "${timeout}" "${retries}" "${start_period}"
  TASK6_IMAGE="${BUILT_IMAGES[0]}" timeout 120 docker compose -p "${project}" \
    -f "${REPOSITORY_ROOT}/docker-compose.yml" -f "${override}" up -d --no-build
  container_id="$(TASK6_IMAGE="${BUILT_IMAGES[0]}" timeout 15 docker compose -p "${project}" \
    -f "${REPOSITORY_ROOT}/docker-compose.yml" -f "${override}" ps -q transcriptlab)"
  [[ -n "${container_id}" ]] || fail "${scenario} Compose scenario did not create transcriptlab"
  wait_for_health_status "${container_id}" "${expected_status}"
  if [[ "${expected_status}" == 'healthy' ]]; then
    timeout 15 docker exec "${container_id}" curl -fsS http://127.0.0.1:5000/api/health >/dev/null \
      || fail 'healthy CPU container does not serve /api/health'
  fi
  TASK6_IMAGE="${BUILT_IMAGES[0]}" timeout 120 docker compose -p "${project}" \
    -f "${REPOSITORY_ROOT}/docker-compose.yml" -f "${override}" down -v --remove-orphans
}

run_cpu_runtime_qa() {
  TEST_DIRECTORY="$(mktemp -d)"
  run_cpu_health_scenario healthy "${HEALTH_COMMAND}" 10s 3s 12 30s healthy
  run_cpu_health_scenario unhealthy 'curl -fsS http://127.0.0.1:5999/api/health >/dev/null || exit 1' 1s 1s 2 0s unhealthy
  printf 'PASS: CPU Compose becomes healthy and a closed probe target becomes unhealthy\n'
}

run_openvino_runtime_qa() {
  local project="task6-health-openvino-$$"
  local override="${TEST_DIRECTORY}/openvino.yml"
  local container_id

  [[ -d /dev/dri ]] || fail 'OpenVINO runtime requested without /dev/dri'
  write_cpu_override "${override}" "${HEALTH_COMMAND}" 10s 3s 12 30s
  TASK6_IMAGE="${BUILT_IMAGES[2]}" timeout 120 docker compose -p "${project}" \
    -f "${REPOSITORY_ROOT}/docker-compose.yml" \
    -f "${REPOSITORY_ROOT}/docker-compose.openvino.yml" \
    -f "${override}" up -d --no-build
  container_id="$(TASK6_IMAGE="${BUILT_IMAGES[2]}" timeout 15 docker compose -p "${project}" \
    -f "${REPOSITORY_ROOT}/docker-compose.yml" \
    -f "${REPOSITORY_ROOT}/docker-compose.openvino.yml" \
    -f "${override}" ps -q transcriptlab)"
  [[ -n "${container_id}" ]] || fail 'OpenVINO Compose scenario did not create transcriptlab'
  wait_for_health_status "${container_id}" healthy
  timeout 15 docker exec "${container_id}" curl -fsS http://127.0.0.1:5000/api/health >/dev/null \
    || fail 'healthy OpenVINO container does not serve /api/health'
  TASK6_IMAGE="${BUILT_IMAGES[2]}" timeout 120 docker compose -p "${project}" \
    -f "${REPOSITORY_ROOT}/docker-compose.yml" \
    -f "${REPOSITORY_ROOT}/docker-compose.openvino.yml" \
    -f "${override}" down -v --remove-orphans
  printf 'PASS: OpenVINO Compose becomes healthy with /dev/dri\n'
}

case "${1:-}" in
  ''|--static)
    verify_static
    printf 'PASS: Docker healthcheck static assertions\n'
    ;;
  --images)
    verify_static
    build_and_inspect_all_images
    ;;
  --cpu-runtime)
    verify_static
    build_and_inspect_all_images
    run_cpu_runtime_qa
    ;;
  --all-runtime)
    verify_static
    build_and_inspect_all_images
    run_cpu_runtime_qa
    run_openvino_runtime_qa
    ;;
  *)
    fail "usage: $0 [--static|--images|--cpu-runtime|--all-runtime]"
    ;;
esac
