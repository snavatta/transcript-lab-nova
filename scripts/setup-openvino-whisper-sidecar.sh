#!/usr/bin/env bash
# Install Python dependencies for the OpenVINO Whisper sidecar.
#
# Usage:
#   ./scripts/setup-openvino-whisper-sidecar.sh [--venv <path>] [--python <path>]
#
# Options:
#   --venv    Path to the Python virtual environment to install into.
#             Uses the active venv ($VIRTUAL_ENV) if set, otherwise creates
#             data/venvs/openvino-whisper in the repo root.
#   --python  Python executable to use when creating a new venv.
#             Defaults to python3.
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

# Create venv if it does not exist
if [[ ! -f "${VENV_PATH}/bin/activate" ]]; then
  echo "Creating virtual environment at $VENV_PATH ..."
  "$PYTHON_BIN" -m venv "$VENV_PATH"
fi

PIP="${VENV_PATH}/bin/pip"
PYTHON="${VENV_PATH}/bin/python"

# ---------------------------------------------------------------------------
# Upgrade pip
# ---------------------------------------------------------------------------

echo "Upgrading pip ..."
"$PIP" install --upgrade pip --quiet

# ---------------------------------------------------------------------------
# Install OpenVINO GenAI if not already present
# ---------------------------------------------------------------------------

echo "Checking for openvino-genai ..."
if ! "$PYTHON" -c "import openvino_genai" 2>/dev/null; then
  echo "Installing openvino-genai ..."
  "$PIP" install openvino-genai
else
  OV_VERSION=$("$PYTHON" -c "import openvino_genai; print(getattr(openvino_genai, '__version__', 'unknown'))" 2>/dev/null || echo "unknown")
  echo "openvino-genai already installed (version: ${OV_VERSION})"
fi

# ---------------------------------------------------------------------------
# Install FastAPI and Uvicorn
# ---------------------------------------------------------------------------

echo "Checking for fastapi ..."
if ! "$PYTHON" -c "import fastapi" 2>/dev/null; then
  echo "Installing fastapi ..."
  "$PIP" install "fastapi[standard]"
else
  FA_VERSION=$("$PYTHON" -c "import fastapi; print(fastapi.__version__)" 2>/dev/null || echo "unknown")
  echo "fastapi already installed (version: ${FA_VERSION})"
fi

echo "Checking for uvicorn ..."
if ! "$PYTHON" -c "import uvicorn" 2>/dev/null; then
  echo "Installing uvicorn ..."
  "$PIP" install uvicorn
else
  UV_VERSION=$("$PYTHON" -c "import uvicorn; print(uvicorn.__version__)" 2>/dev/null || echo "unknown")
  echo "uvicorn already installed (version: ${UV_VERSION})"
fi

# ---------------------------------------------------------------------------
# Verify the sidecar script can be imported
# ---------------------------------------------------------------------------

SIDECAR_SCRIPT="${REPO_ROOT}/src/ClassTranscriber.Api/Tools/openvino_whisper_sidecar.py"

if [[ -f "$SIDECAR_SCRIPT" ]]; then
  echo "Verifying sidecar imports ..."
  if "$PYTHON" -c "import openvino_genai; import fastapi; import uvicorn; import numpy; print('All imports OK')"; then
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
echo ""
echo "To run TranscriptLab Nova with this venv, activate it before starting dotnet:"
echo ""
echo "  source ${VENV_PATH}/bin/activate"
echo "  dotnet run --project src/ClassTranscriber.Api"
echo ""
echo "With the venv active, the default PythonPath value of 'python3' in appsettings.json"
echo "will resolve to this venv's interpreter automatically. No config changes are needed."
