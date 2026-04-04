"""
Pytest fixtures for OpenVINO sidecar tests.

Provides:
- sidecar_process: base URL of the running sidecar (starts the process if needed)
- http_client: httpx.Client pointed at the sidecar
- test_wav_path: path to a 16kHz mono WAV file (real or synthetic silent)
- has_speech_audio: True when SIDECAR_TEST_AUDIO_PATH is set and points to a real audio file

To test with real speech audio (required for non-empty transcription assertions):
    SIDECAR_TEST_AUDIO_PATH=/path/to/audio.ogg pytest tests/ -m gpu -v
    SIDECAR_TEST_AUDIO_PATH=/path/to/audio.wav pytest tests/ -m gpu -v

Supported audio formats: WAV (used directly), OGG/Opus/MP3 (converted via ffmpeg).
ffmpeg must be installed for non-WAV input files.
"""

import io
import os
import struct
import subprocess
import sys
import tempfile
import time
import wave
from pathlib import Path

import httpx
import pytest

# Path to the sidecar script relative to this test file
_SIDECAR_SCRIPT = Path(__file__).parent.parent / "openvino_whisper_sidecar.py"

# Where the sidecar should store models during tests — use a temp dir per session
_TEST_MODELS_PATH: str | None = None

# Python used to run the sidecar (same interpreter running the tests, or env var override)
_PYTHON = os.environ.get("SIDECAR_PYTHON", sys.executable)

# Port for the test sidecar (avoid colliding with production port 15432)
_SIDECAR_PORT = int(os.environ.get("SIDECAR_TEST_PORT", "15433"))

# Optional: reuse a pre-populated models directory (skips downloads)
_SIDECAR_TEST_MODELS_PATH = os.environ.get("SIDECAR_TEST_MODELS_PATH", "").strip() or None

# Optional path to a real speech audio file for transcription content tests
_SIDECAR_TEST_AUDIO = os.environ.get("SIDECAR_TEST_AUDIO_PATH", "").strip() or None


def _convert_to_wav(input_path: str, output_dir: str) -> str | None:
    """Convert an audio file to 16kHz mono WAV using ffmpeg. Returns output path or None on failure."""
    if input_path.lower().endswith(".wav"):
        return input_path
    output_path = os.path.join(output_dir, "converted_audio.wav")
    try:
        result = subprocess.run(
            [
                "ffmpeg", "-y", "-i", input_path,
                "-ar", "16000", "-ac", "1", "-f", "wav",
                output_path,
            ],
            capture_output=True,
            timeout=30,
        )
        if result.returncode == 0:
            return output_path
        sys.stderr.write(
            f"conftest: ffmpeg conversion failed:\n{result.stderr.decode(errors='replace')}\n"
        )
        return None
    except FileNotFoundError:
        sys.stderr.write("conftest: ffmpeg not found; cannot convert non-WAV audio.\n")
        return None
    except subprocess.TimeoutExpired:
        sys.stderr.write("conftest: ffmpeg conversion timed out.\n")
        return None


def _make_test_wav(path: str, duration_seconds: float = 3.0, sample_rate: int = 16000) -> None:
    """Generate a silent 16kHz mono int16 WAV file at the given path."""
    n_frames = int(sample_rate * duration_seconds)
    with wave.open(path, "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)  # int16
        wf.setframerate(sample_rate)
        wf.writeframes(struct.pack(f"<{n_frames}h", *([0] * n_frames)))


def _wait_for_health(base_url: str, timeout: float = 60.0) -> bool:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        try:
            response = httpx.get(f"{base_url}/health", timeout=2.0)
            if response.status_code == 200:
                return True
        except Exception:
            pass
        time.sleep(0.5)
    return False


@pytest.fixture(scope="session")
def sidecar_temp_dir():
    with tempfile.TemporaryDirectory(prefix="ov_sidecar_test_") as tmp:
        yield tmp


@pytest.fixture(scope="session")
def sidecar_process(sidecar_temp_dir):
    """Start the sidecar subprocess, wait for health, and yield its base URL. Session-scoped."""
    models_path = _SIDECAR_TEST_MODELS_PATH or os.path.join(sidecar_temp_dir, "models")
    if not _SIDECAR_TEST_MODELS_PATH:
        os.makedirs(models_path, exist_ok=True)

    proc = subprocess.Popen(
        [
            _PYTHON,
            str(_SIDECAR_SCRIPT),
            "--port", str(_SIDECAR_PORT),
            "--host", "127.0.0.1",
            "--models-path", models_path,
            "--log-segments",
        ],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )

    base_url = f"http://127.0.0.1:{_SIDECAR_PORT}"
    ready = _wait_for_health(base_url, timeout=60.0)

    if not ready:
        # Capture stderr for debugging before terminating
        proc.terminate()
        try:
            _, stderr = proc.communicate(timeout=5)
        except Exception:
            stderr = b""
        pytest.fail(
            f"Sidecar did not become healthy within 60 seconds.\n"
            f"stderr:\n{stderr.decode(errors='replace')}"
        )

    yield base_url

    proc.terminate()
    try:
        proc.wait(timeout=10)
    except subprocess.TimeoutExpired:
        proc.kill()


@pytest.fixture(scope="session")
def http_client(sidecar_process):
    """httpx.Client with the sidecar base URL and a generous timeout."""
    with httpx.Client(base_url=sidecar_process, timeout=300.0) as client:
        yield client


@pytest.fixture(scope="session")
def test_wav_path(sidecar_temp_dir):
    """Path to a 16kHz mono WAV file for transcription tests.

    If SIDECAR_TEST_AUDIO_PATH env var is set, uses that file (converting from OGG/Opus if needed
    via ffmpeg). Otherwise, generates a 3-second silent WAV (structural tests only).
    """
    if _SIDECAR_TEST_AUDIO:
        if not os.path.isfile(_SIDECAR_TEST_AUDIO):
            sys.stderr.write(
                f"conftest: SIDECAR_TEST_AUDIO_PATH={_SIDECAR_TEST_AUDIO!r} does not exist; "
                "falling back to synthetic silence.\n"
            )
        else:
            converted = _convert_to_wav(_SIDECAR_TEST_AUDIO, sidecar_temp_dir)
            if converted:
                return converted
            sys.stderr.write("conftest: audio conversion failed; falling back to synthetic silence.\n")

    path = os.path.join(sidecar_temp_dir, "test_audio.wav")
    _make_test_wav(path, duration_seconds=3.0)
    return path


@pytest.fixture(scope="session")
def has_speech_audio(sidecar_temp_dir):
    """True when SIDECAR_TEST_AUDIO_PATH is set and resolved to a usable file."""
    if not _SIDECAR_TEST_AUDIO or not os.path.isfile(_SIDECAR_TEST_AUDIO):
        return False
    converted = _convert_to_wav(_SIDECAR_TEST_AUDIO, sidecar_temp_dir)
    return converted is not None
