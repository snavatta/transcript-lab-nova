#!/usr/bin/env bash
set -euo pipefail

readonly TEST_DIR="$(mktemp -d)"
trap 'rm -rf -- "${TEST_DIR}"' EXIT

mkdir -p "${TEST_DIR}/bin" "${TEST_DIR}/profiles"
printf '%s\n' 'CONTROL_PLANE_API_KEY=test-key' > "${TEST_DIR}/test.env"

printf '%s\n' \
  '#!/usr/bin/env bash' \
  'set -euo pipefail' \
  '[[ "${CONTROL_PLANE_API_KEY}" == "test-key" ]]' \
  '[[ "$#" -eq 7 ]]' \
  '[[ "$1" == "run" ]]' \
  '[[ "$2" == "--profile" ]]' \
  '[[ "$3" == "transcriptlab" ]]' \
  '[[ "$4" == "--profile-dir" ]]' \
  '[[ "$5" == "'"${TEST_DIR}"'/profiles" ]]' \
  '[[ "$6" == "--health.url-file" ]]' \
  '[[ "$7" == "'"${TEST_DIR}"'/profiles/transcriptlab-health.url" ]]' \
  > "${TEST_DIR}/bin/tunnel-client"
chmod +x "${TEST_DIR}/bin/tunnel-client"

PATH="${TEST_DIR}/bin:${PATH}" \
TUNNEL_CLIENT_ENV_FILE="${TEST_DIR}/test.env" \
  "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)/scripts/start-tunnel-client.sh" \
  "${TEST_DIR}/profiles/transcriptlab"
