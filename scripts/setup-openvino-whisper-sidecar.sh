#!/usr/bin/env bash
# Install Python dependencies for the OpenVINO Whisper sidecar.
#
# Usage:
#   ./scripts/setup-openvino-whisper-sidecar.sh [--venv <path>] [--python <path>]
#       [--requirements <path>] [--include-test-deps]
#       [--openvino-version <version>] [--openvino-tokenizers-version <version>]
#       [--openvino-genai-version <version>]
#
# Options:
#   --venv    Path to the Python virtual environment to install into.
#             Uses the active venv ($VIRTUAL_ENV) if set, otherwise creates
#             data/venvs/openvino-whisper in the repo root.
#   --python  Python executable to use when creating a new venv.
#             Defaults to python3.
#   --requirements  Requirements file to install from.
#             Defaults to src/ClassTranscriber.Api/Tools/requirements-openvino-sidecar.txt.
#   --include-test-deps  Install test-only dependencies from the requirements file.
#   --openvino-version  Install a pinned openvino package version.
#   --openvino-tokenizers-version  Install a pinned openvino-tokenizers package version.
#   --openvino-genai-version  Install a pinned openvino-genai package version.
#
# Environment variables:
#   VIRTUAL_ENV  Honoured automatically if a venv is already activated.
#
# Examples:
#   # Install into an existing venv
#   source /home/user/whisper-ov-venv/bin/activate
#   ./scripts/setup-openvino-whisper-sidecar.sh
#
#   # Create a fresh venv and install (default — no --venv flag needed)
#   ./scripts/setup-openvino-whisper-sidecar.sh
#
#   # Use a specific Python interpreter
#   ./scripts/setup-openvino-whisper-sidecar.sh --python python3.12

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

# ---------------------------------------------------------------------------
# Argument parsing
# ---------------------------------------------------------------------------

VENV_PATH=""
PYTHON_BIN="python3"
REQUIREMENTS_FILE=""
INCLUDE_TEST_DEPS=0
OPENVINO_VERSION=""
OPENVINO_TOKENIZERS_VERSION=""
OPENVINO_GENAI_VERSION=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --venv)
      VENV_PATH="$2"
      shift 2
      ;;
    --python)
      PYTHON_BIN="$2"
      shift 2
      ;;
    --requirements)
      REQUIREMENTS_FILE="$2"
      shift 2
      ;;
    --include-test-deps)
      INCLUDE_TEST_DEPS=1
      shift 1
      ;;
    --openvino-version)
      OPENVINO_VERSION="$2"
      shift 2
      ;;
    --openvino-tokenizers-version)
      OPENVINO_TOKENIZERS_VERSION="$2"
      shift 2
      ;;
    --openvino-genai-version)
      OPENVINO_GENAI_VERSION="$2"
      shift 2
      ;;
    *)
      echo "Unknown option: $1" >&2
      exit 1
      ;;
  esac
done

# ---------------------------------------------------------------------------
# Resolve venv
# ---------------------------------------------------------------------------

if [[ -z "$VENV_PATH" ]]; then
  if [[ -n "${VIRTUAL_ENV:-}" ]]; then
    VENV_PATH="$VIRTUAL_ENV"
    echo "Using active virtual environment: $VENV_PATH"
  else
    VENV_PATH="${REPO_ROOT}/data/venvs/openvino-whisper"
    echo "No venv specified. Will create: $VENV_PATH"
  fi
fi

if [[ -z "$REQUIREMENTS_FILE" ]]; then
  REQUIREMENTS_FILE="${REPO_ROOT}/src/ClassTranscriber.Api/Tools/requirements-openvino-sidecar.txt"
fi

# Create venv if it does not exist
if [[ ! -f "${VENV_PATH}/bin/activate" ]]; then
  echo "Creating virtual environment at $VENV_PATH ..."
  "$PYTHON_BIN" -m venv "$VENV_PATH"
fi

PIP="${VENV_PATH}/bin/pip"
PYTHON="${VENV_PATH}/bin/python"

declare -a RUNTIME_REQUIREMENTS=()

read_runtime_requirements() {
  local line

  while IFS= read -r line || [[ -n "$line" ]]; do
    if [[ "$line" == "# Test dependencies" && "$INCLUDE_TEST_DEPS" -eq 0 ]]; then
      break
    fi

    if [[ -z "$line" || "$line" =~ ^# ]]; then
      continue
    fi

    case "$line" in
      openvino|openvino==*|openvino-tokenizers|openvino-tokenizers==*|openvino-genai|openvino-genai==*)
        continue
        ;;
    esac

    RUNTIME_REQUIREMENTS+=("$line")
  done < "$REQUIREMENTS_FILE"
}

# ---------------------------------------------------------------------------
# Upgrade pip
# ---------------------------------------------------------------------------

echo "Upgrading pip ..."
"$PIP" install --upgrade pip --quiet

# ---------------------------------------------------------------------------
# Install dependencies
# ---------------------------------------------------------------------------

if [[ ! -f "$REQUIREMENTS_FILE" ]]; then
  echo "Requirements file not found: ${REQUIREMENTS_FILE}" >&2
  exit 1
fi

read_runtime_requirements

declare -a OV_PACKAGES=()

if [[ -n "$OPENVINO_VERSION" ]]; then
  OV_PACKAGES+=("openvino==${OPENVINO_VERSION}")
else
  OV_PACKAGES+=("openvino")
fi

if [[ -n "$OPENVINO_TOKENIZERS_VERSION" ]]; then
  OV_PACKAGES+=("openvino-tokenizers==${OPENVINO_TOKENIZERS_VERSION}")
else
  OV_PACKAGES+=("openvino-tokenizers")
fi

if [[ -n "$OPENVINO_GENAI_VERSION" ]]; then
  OV_PACKAGES+=("openvino-genai==${OPENVINO_GENAI_VERSION}")
else
  OV_PACKAGES+=("openvino-genai")
fi

echo "Installing OpenVINO packages ..."
"$PIP" install --no-cache-dir "${OV_PACKAGES[@]}"

if (( ${#RUNTIME_REQUIREMENTS[@]} > 0 )); then
  echo "Installing sidecar dependencies from ${REQUIREMENTS_FILE} ..."
  "$PIP" install --no-cache-dir "${RUNTIME_REQUIREMENTS[@]}"
fi

if [[ "$INCLUDE_TEST_DEPS" -eq 1 ]]; then
  echo "Installing test dependencies from ${REQUIREMENTS_FILE} ..."
  "$PIP" install --no-cache-dir -r "$REQUIREMENTS_FILE"
fi

# ---------------------------------------------------------------------------
# Verify the sidecar script can be imported
# ---------------------------------------------------------------------------

SIDECAR_SCRIPT="${REPO_ROOT}/src/ClassTranscriber.Api/Tools/openvino_whisper_sidecar.py"

if [[ -f "$SIDECAR_SCRIPT" ]]; then
  echo "Verifying sidecar imports ..."
  if "$PYTHON" -c "import openvino; import openvino_tokenizers; import openvino_genai; import fastapi; import uvicorn; import numpy; print('All imports OK')"; then
    echo "Sidecar dependencies verified successfully."
  else
    echo "ERROR: One or more sidecar imports failed. Check the output above." >&2
    exit 1
  fi
else
  echo "WARNING: Sidecar script not found at ${SIDECAR_SCRIPT} — skipping import check."
fi

# ---------------------------------------------------------------------------
# Print summary
# ---------------------------------------------------------------------------

echo ""
echo "Setup complete."
echo ""
echo "Python:  ${PYTHON}"
echo "Venv:    ${VENV_PATH}"
echo "Requirements: ${REQUIREMENTS_FILE}"
echo ""
echo "To run TranscriptLab Nova with this venv, activate it before starting dotnet:"
echo ""
echo "  source ${VENV_PATH}/bin/activate"
echo "  dotnet run --project src/ClassTranscriber.Api"
echo ""
echo "With the venv active, the default PythonPath value of 'python3' in appsettings.json"
echo "will resolve to this venv's interpreter automatically. No config changes are needed."
