#!/usr/bin/env bash
# Install system dependencies and Python packages needed to export a Whisper
# model to OpenVINO format using Optimum-Intel.
#
# Usage:
#   ./scripts/setup-openvino-export-env.sh [--venv <path>] [--python <path>]
#
# Options:
#   --venv    Path to the Python virtual environment to install into.
#             Uses the active venv ($VIRTUAL_ENV) if set, otherwise creates
#             ~/ov-export-env.
#   --python  Python interpreter to use when creating a new venv.
#             Defaults to python3.
#
# Requirements:
#   - Ubuntu 22.04 or later (uses apt)
#   - sudo access (for system package installation)
#   - Internet access
#
# Examples:
#   # Use defaults (creates ~/ov-export-env)
#   ./scripts/setup-openvino-export-env.sh
#
#   # Install into a specific path
#   ./scripts/setup-openvino-export-env.sh --venv /opt/ov-export-env
#
#   # Use a specific Python interpreter
#   ./scripts/setup-openvino-export-env.sh --python python3.12

set -euo pipefail

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
# System packages
# ---------------------------------------------------------------------------

echo "Installing system packages ..."
sudo apt-get update -qq
sudo apt-get install -y --no-install-recommends \
  python3 \
  python3-pip \
  python3-venv \
  git

# ---------------------------------------------------------------------------
# Resolve venv path
# ---------------------------------------------------------------------------

if [[ -z "$VENV_PATH" ]]; then
  if [[ -n "${VIRTUAL_ENV:-}" ]]; then
    VENV_PATH="$VIRTUAL_ENV"
    echo "Using active virtual environment: $VENV_PATH"
  else
    VENV_PATH="${HOME}/ov-export-env"
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
# Install Optimum-Intel (includes optimum-cli and OpenVINO backend)
# ---------------------------------------------------------------------------

echo "Checking for optimum-intel ..."
if ! "$PYTHON" -c "import optimum.intel" 2>/dev/null; then
  echo "Installing optimum[openvino] ..."
  "$PIP" install "optimum[openvino]"
else
  OPT_VERSION=$("$PYTHON" -c "import optimum; print(optimum.__version__)" 2>/dev/null || echo "unknown")
  echo "optimum already installed (version: ${OPT_VERSION})"
fi

# ---------------------------------------------------------------------------
# Install NNCF (weight-only quantization)
# ---------------------------------------------------------------------------

echo "Checking for nncf ..."
if ! "$PYTHON" -c "import nncf" 2>/dev/null; then
  echo "Installing nncf ..."
  "$PIP" install nncf
else
  NNCF_VERSION=$("$PYTHON" -c "import nncf; print(nncf.__version__)" 2>/dev/null || echo "unknown")
  echo "nncf already installed (version: ${NNCF_VERSION})"
fi

# ---------------------------------------------------------------------------
# Install huggingface_hub (for hf_transfer / progress reporting)
# ---------------------------------------------------------------------------

echo "Checking for huggingface_hub ..."
if ! "$PYTHON" -c "import huggingface_hub" 2>/dev/null; then
  echo "Installing huggingface_hub ..."
  "$PIP" install huggingface_hub
else
  HF_VERSION=$("$PYTHON" -c "import huggingface_hub; print(huggingface_hub.__version__)" 2>/dev/null || echo "unknown")
  echo "huggingface_hub already installed (version: ${HF_VERSION})"
fi

# ---------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------

echo ""
echo "Setup complete."
echo ""
echo "Virtual environment: $VENV_PATH"
echo "To activate it manually:"
echo "  source ${VENV_PATH}/bin/activate"
echo ""
echo "Next step:"
echo "  ./scripts/export-whisper-turbo-openvino.sh --venv ${VENV_PATH}"
