#!/usr/bin/env bash
set -euo pipefail

readonly SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly REPOSITORY_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
readonly ENV_FILE="${TUNNEL_CLIENT_ENV_FILE:-${REPOSITORY_ROOT}/.env}"
readonly RELEASE_URLS="https://github.com/openai/tunnel-client/releases/latest/download/PUBLIC_URLS.txt"

die() {
  echo "Error: $*" >&2
  exit 1
}

trim() {
  local value="$1"
  value="${value#"${value%%[![:space:]]*}"}"
  value="${value%"${value##*[![:space:]]}"}"
  printf '%s' "${value}"
}

load_control_plane_api_key() {
  [[ -f "${ENV_FILE}" ]] || die "${ENV_FILE} was not found. Create it from .env.example and set CONTROL_PLANE_API_KEY."

  local line value="" matches=0
  while IFS= read -r line || [[ -n "${line}" ]]; do
    line="${line%$'\r'}"
    if [[ "${line}" =~ ^[[:space:]]*(export[[:space:]]+)?CONTROL_PLANE_API_KEY[[:space:]]*=(.*)$ ]]; then
      ((matches += 1))
      value="$(trim "${BASH_REMATCH[2]}")"
    fi
  done < "${ENV_FILE}"

  [[ ${matches} -eq 1 ]] || die "${ENV_FILE} must contain exactly one CONTROL_PLANE_API_KEY assignment."

  if [[ "${value}" == \"*\" ]] || [[ "${value}" == \'*\' ]]; then
    local quote="${value:0:1}"
    [[ ${#value} -ge 2 && "${value: -1}" == "${quote}" ]] || die "CONTROL_PLANE_API_KEY has mismatched quotes."
    value="${value:1:${#value}-2}"
  fi

  [[ -n "${value}" ]] || die "CONTROL_PLANE_API_KEY is empty."
  [[ "${value}" != "YOUR_API_KEY" && "${value}" != "changeme" ]] || die "Replace the placeholder CONTROL_PLANE_API_KEY in ${ENV_FILE}."
  [[ "${value}" != *[[:space:]]* ]] || die "CONTROL_PLANE_API_KEY must not contain whitespace."

  export CONTROL_PLANE_API_KEY="${value}"
}

platform_archive_suffix() {
  local operating_system architecture
  case "$(uname -s)" in
    Linux) operating_system="linux" ;;
    Darwin) operating_system="darwin" ;;
    *) die "Only Linux and macOS are supported by this installer." ;;
  esac

  case "$(uname -m)" in
    x86_64 | amd64) architecture="amd64" ;;
    arm64 | aarch64) architecture="arm64" ;;
    *) die "Unsupported CPU architecture: $(uname -m)" ;;
  esac

  printf '%s' "-${operating_system}-${architecture}.zip"
}

find_release_url() {
  local urls_file="$1" suffix="$2" url
  while IFS= read -r url; do
    if [[ "${url}" == *"${suffix}" ]]; then
      printf '%s' "${url}"
      return 0
    fi
  done < "${urls_file}"
  return 1
}

sha256_file() {
  local file="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "${file}" | awk '{print $1}'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "${file}" | awk '{print $1}'
  else
    die "sha256sum or shasum is required to verify the tunnel-client download."
  fi
}

install_tunnel_client() (
  command -v curl >/dev/null 2>&1 || die "curl is required to install tunnel-client."
  command -v unzip >/dev/null 2>&1 || die "unzip is required to install tunnel-client."

  local install_dir="$1" download_dir urls_file archive_url checksum_url
  local archive_file checksum_file archive_name expected_checksum actual_checksum
  download_dir="$(mktemp -d)"
  trap 'rm -rf -- "${download_dir}"' EXIT

  urls_file="${download_dir}/PUBLIC_URLS.txt"
  curl -fsSL "${RELEASE_URLS}" -o "${urls_file}"

  archive_url="$(find_release_url "${urls_file}" "$(platform_archive_suffix)")" \
    || die "The latest tunnel-client release has no archive for this platform."
  checksum_url="$(find_release_url "${urls_file}" "/SHA256SUMS.txt")" \
    || die "The latest tunnel-client release has no checksum file."

  archive_name="${archive_url##*/}"
  archive_file="${download_dir}/${archive_name}"
  checksum_file="${download_dir}/SHA256SUMS.txt"

  echo "Installing the latest tunnel-client release into ${install_dir}..." >&2
  curl -fsSL "${archive_url}" -o "${archive_file}"
  curl -fsSL "${checksum_url}" -o "${checksum_file}"

  expected_checksum="$(awk -v name="${archive_name}" '$2 == name { print $1; exit }' "${checksum_file}")"
  [[ -n "${expected_checksum}" ]] || die "No published checksum was found for ${archive_name}."
  actual_checksum="$(sha256_file "${archive_file}")"
  [[ "${actual_checksum}" == "${expected_checksum}" ]] || die "Checksum verification failed for ${archive_name}."

  unzip -q "${archive_file}" -d "${download_dir}/release"
  [[ -x "${download_dir}/release/tunnel-client" ]] || die "The release archive does not contain tunnel-client."
  [[ -x "${download_dir}/release/cloudflared" ]] || die "The release archive does not contain its cloudflared companion."

  mkdir -p "${install_dir}"
  install -m 0755 "${download_dir}/release/tunnel-client" "${install_dir}/tunnel-client"
  install -m 0755 "${download_dir}/release/cloudflared" "${install_dir}/cloudflared"
  install -m 0644 "${download_dir}/release/cloudflared-manifest.json" "${install_dir}/cloudflared-manifest.json"
  install -m 0644 "${download_dir}/release/LICENSE" "${install_dir}/LICENSE"

  printf '%s' "${install_dir}/tunnel-client"
)

resolve_tunnel_client() {
  local installed
  if installed="$(command -v tunnel-client 2>/dev/null)"; then
    printf '%s' "${installed}"
    return
  fi

  [[ -n "${HOME:-}" ]] || die "HOME must be set to select an external installation directory."
  local install_dir="${TUNNEL_CLIENT_INSTALL_DIR:-${HOME}/.local/lib/transcriptlab-tunnel-client}"
  [[ "${install_dir}" == /* && "${install_dir}" != "/" ]] || die "TUNNEL_CLIENT_INSTALL_DIR must be an absolute, non-root path."
  case "${install_dir}" in
    "${REPOSITORY_ROOT}" | "${REPOSITORY_ROOT}"/*)
      die "tunnel-client must be installed outside this public repository."
      ;;
  esac
  local local_client="${install_dir}/tunnel-client"
  if [[ -x "${local_client}" ]]; then
    printf '%s' "${local_client}"
    return
  fi

  install_tunnel_client "${install_dir}"
}

main() {
  [[ $# -le 1 ]] || die "Usage: $0 [absolute-profile-path]"
  load_control_plane_api_key

  [[ -n "${HOME:-}" ]] || die "HOME must be set to select the default external profile."
  local profile_path="${1:-${TUNNEL_CLIENT_PROFILE:-${XDG_CONFIG_HOME:-${HOME}/.config}/transcriptlab/tunnel-client/transcriptlab}}"
  [[ "${profile_path}" == /* ]] || die "The tunnel profile path must be absolute."
  case "${profile_path}" in
    "${REPOSITORY_ROOT}" | "${REPOSITORY_ROOT}"/*)
      die "The tunnel profile must be stored outside this public repository."
      ;;
  esac

  local profile_dir profile_name
  profile_dir="$(dirname -- "${profile_path}")"
  profile_name="$(basename -- "${profile_path}")"

  local client
  client="$(resolve_tunnel_client)"
  echo "Starting tunnel-client with profile ${profile_name} from ${profile_dir}."
  exec "${client}" run \
    --profile "${profile_name}" \
    --profile-dir "${profile_dir}" \
    --health.url-file "${profile_dir}/${profile_name}-health.url"
}

main "$@"
