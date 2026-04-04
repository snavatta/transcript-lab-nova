"""
Unit tests for helper functions in openvino_genai_worker.py.

These tests do not require a GPU, OpenVINO, or any model files.
They exercise pure Python logic only.

Run with:
    pytest tests/test_worker_unit.py -v
"""

import io
import struct
import sys
import wave
from pathlib import Path
from unittest.mock import MagicMock, patch

import pytest

# ---------------------------------------------------------------------------
# Import the worker module under test
# ---------------------------------------------------------------------------

_WORKER_PATH = Path(__file__).parent.parent / "openvino_genai_worker.py"


def _import_worker():
    """Import openvino_genai_worker without executing its main block."""
    import importlib.util

    # Stub openvino, openvino_genai, numpy if not installed (GPU-less CI)
    stubs = {}
    for mod_name in ("openvino", "openvino_genai", "numpy"):
        try:
            __import__(mod_name)
        except ImportError:
            stub = MagicMock()
            sys.modules[mod_name] = stub
            stubs[mod_name] = stub

    spec = importlib.util.spec_from_file_location("openvino_genai_worker", str(_WORKER_PATH))
    module = importlib.util.module_from_spec(spec)
    # __name__ stays as "openvino_genai_worker" so the if __name__ == "__main__" guard fires correctly
    spec.loader.exec_module(module)

    # Remove stubs after import so we don't pollute other tests
    for mod_name in stubs:
        sys.modules.pop(mod_name, None)

    return module


_worker = _import_worker()


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------

class TestSanitizeText:
    def test_strips_newlines(self):
        assert _worker.sanitize_text("hello\nworld") == "hello world"

    def test_strips_carriage_returns(self):
        assert _worker.sanitize_text("hello\r\nworld\r\n") == "hello world"

    def test_collapses_spaces(self):
        assert _worker.sanitize_text("  hello   world  ") == "hello world"

    def test_empty_string(self):
        assert _worker.sanitize_text("") == ""

    def test_only_whitespace(self):
        assert _worker.sanitize_text("   \n\r\t   ") == ""


class TestBuildSegments:
    def _make_chunk(self, text: str, start_ts: float, end_ts: float):
        chunk = MagicMock()
        chunk.text = text
        chunk.start_ts = start_ts
        chunk.end_ts = end_ts
        return chunk

    def test_normal_chunks(self):
        chunks = [
            self._make_chunk("Hello", 0.0, 1.5),
            self._make_chunk("World", 1.5, 3.0),
        ]
        segments = _worker.build_segments(chunks, duration_ms=3000, plain_text="", log_segments=False)
        assert len(segments) == 2
        assert segments[0]["startMs"] == 0
        assert segments[0]["endMs"] == 1500
        assert segments[0]["text"] == "Hello"
        assert segments[1]["startMs"] == 1500
        assert segments[1]["endMs"] == 3000
        assert segments[1]["text"] == "World"

    def test_fallback_to_plain_text_when_no_chunks(self):
        """When chunks is empty and plain_text is provided, one segment should be returned."""
        segments = _worker.build_segments(
            [], duration_ms=5000, plain_text="Fallback text", log_segments=False
        )
        assert len(segments) == 1
        assert segments[0]["startMs"] == 0
        assert segments[0]["endMs"] == 5000
        assert segments[0]["text"] == "Fallback text"

    def test_empty_chunks_and_no_plain_text_returns_empty(self):
        segments = _worker.build_segments([], duration_ms=5000, plain_text="", log_segments=False)
        assert segments == []

    def test_skips_chunks_with_empty_text(self):
        chunks = [
            self._make_chunk("  ", 0.0, 1.0),  # whitespace only — should be skipped
            self._make_chunk("Real text", 1.0, 2.0),
        ]
        segments = _worker.build_segments(chunks, duration_ms=2000, plain_text="", log_segments=False)
        assert len(segments) == 1
        assert segments[0]["text"] == "Real text"

    def test_clamps_end_ms_below_start_ms(self):
        chunks = [self._make_chunk("Problematic", 2.0, 1.0)]  # end < start
        segments = _worker.build_segments(chunks, duration_ms=3000, plain_text="", log_segments=False)
        assert len(segments) == 1
        assert segments[0]["endMs"] >= segments[0]["startMs"]


class TestLoadWave:
    def _make_wav_bytes(
        self,
        channels: int = 1,
        sample_rate: int = 16000,
        sample_width: int = 2,
        duration_seconds: float = 1.0,
    ) -> str:
        """Write a WAV file to a temp path and return the path."""
        import tempfile, os
        n_frames = int(sample_rate * duration_seconds)
        path = tempfile.mktemp(suffix=".wav")
        with wave.open(path, "wb") as wf:
            wf.setnchannels(channels)
            wf.setsampwidth(sample_width)
            wf.setframerate(sample_rate)
            fmt = "h" if sample_width == 2 else ("b" if sample_width == 1 else "i")
            wf.writeframes(struct.pack(f"<{n_frames}{fmt}", *([0] * n_frames)))
        return path

    def test_load_16khz_mono_int16(self):
        path = self._make_wav_bytes(channels=1, sample_rate=16000, sample_width=2, duration_seconds=2.0)
        request = {"audioPath": path}
        # Patch pipeline to avoid actual inference
        # We only test load_wave helper via build_response → but load_wave is also callable directly
        # The worker exposes load_wave as a module-level function
        samples, duration_ms = _worker.load_wave(path)
        assert samples is not None
        assert len(samples) == 32000  # 16000 * 2 seconds
        assert duration_ms == 2000

    def test_rejects_stereo(self):
        path = self._make_wav_bytes(channels=2, sample_rate=16000, sample_width=2, duration_seconds=1.0)
        with pytest.raises(RuntimeError, match="mono"):
            _worker.load_wave(path)

    def test_rejects_wrong_sample_rate(self):
        path = self._make_wav_bytes(channels=1, sample_rate=44100, sample_width=2, duration_seconds=1.0)
        with pytest.raises(RuntimeError, match="16kHz"):
            _worker.load_wave(path)
