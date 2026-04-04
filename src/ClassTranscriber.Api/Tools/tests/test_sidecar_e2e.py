"""
End-to-end tests for the OpenVINO Whisper sidecar.

These tests require:
- An Intel Arc A310 (or other OpenVINO-compatible GPU) on the host
- The Python environment configured in SIDECAR_PYTHON env var (or sys.executable)
  must have openvino_genai, fastapi, uvicorn, numpy installed
- Internet access for the initial model download test
- Set SIDECAR_TEST_PORT env var to override the default test port (15433)

Run GPU tests:
    pytest tests/test_sidecar_e2e.py -m gpu -v

Run all tests (including GPU):
    pytest tests/test_sidecar_e2e.py -v

Skip GPU tests:
    pytest tests/test_sidecar_e2e.py -m "not gpu" -v
"""

import time

import httpx
import pytest

# Model used in most tests; can be overridden with SIDECAR_TEST_MODEL env var.
# Use "base-int8" if tiny-int8 is not pre-installed and you want to skip download.
import os
_TEST_MODEL = os.environ.get("SIDECAR_TEST_MODEL", "tiny-int8")


class TestHealth:
    def test_health(self, http_client: httpx.Client):
        """GET /health must return 200 and {"status": "ok"}."""
        response = http_client.get("/health")
        assert response.status_code == 200
        assert response.json() == {"status": "ok"}


@pytest.mark.gpu
class TestDevices:
    def test_devices_endpoint_ok(self, http_client: httpx.Client):
        """GET /devices must return 200."""
        response = http_client.get("/devices")
        assert response.status_code == 200

    def test_devices_includes_gpu(self, http_client: httpx.Client):
        """GET /devices must include at least one device with id starting with 'GPU'."""
        response = http_client.get("/devices")
        assert response.status_code == 200
        devices = response.json()["devices"]
        gpu_devices = [d for d in devices if d["id"].upper().startswith("GPU")]
        assert gpu_devices, (
            f"No GPU device found. Available devices: {[d['id'] for d in devices]}"
        )


@pytest.mark.gpu
class TestModelDownload:
    def test_model_download_sse(self, http_client: httpx.Client):
        """POST /models/download streams SSE events and ends with status=complete."""
        events = []
        with http_client.stream(
            "POST", "/models/download", json={"model": _TEST_MODEL}, timeout=300.0
        ) as response:
            assert response.status_code == 200
            assert "text/event-stream" in response.headers.get("content-type", "")
            for line in response.iter_lines():
                if line.startswith("data:"):
                    payload = line[len("data:"):].strip()
                    import json
                    event = json.loads(payload)
                    events.append(event)
                    if event.get("status") in ("complete", "error"):
                        break

        statuses = [e["status"] for e in events]
        assert "complete" in statuses, f"Expected 'complete' in SSE events. Got: {statuses}"
        assert "error" not in statuses, f"Download reported error: {events}"

    def test_model_list_after_download(self, http_client: httpx.Client):
        """GET /models after download must include tiny-int8 with is_installed=true."""
        response = http_client.get("/models")
        assert response.status_code == 200
        models = {m["name"]: m for m in response.json()["models"]}
        assert _TEST_MODEL in models, f"Expected {_TEST_MODEL} in model list"
        assert models[_TEST_MODEL]["is_installed"] is True, (
            f"Expected {_TEST_MODEL} to be installed: {models[_TEST_MODEL]}"
        )


@pytest.mark.gpu
class TestTranscription:
    def test_transcribe_internal(self, http_client: httpx.Client, test_wav_path: str):
        """POST /transcribe with a WAV file returns non-empty result."""
        response = http_client.post(
            "/transcribe",
            json={
                "audio_path": test_wav_path,
                "model_path": _get_model_path(http_client),
                "device": "GPU",
                "language_mode": "Auto",
            },
        )
        assert response.status_code == 200, f"Unexpected status: {response.status_code} {response.text}"
        body = response.json()
        assert "duration_ms" in body
        assert body["duration_ms"] > 0
        # segments list may be empty for silent audio, that is acceptable
        assert "segments" in body
        assert "plain_text" in body

    def test_transcribe_openai_compatible(self, http_client: httpx.Client, test_wav_path: str):
        """POST /v1/audio/transcriptions (multipart, verbose_json) returns expected fields."""
        with open(test_wav_path, "rb") as f:
            audio_bytes = f.read()

        response = http_client.post(
            "/v1/audio/transcriptions",
            files={"file": ("audio.wav", audio_bytes, "audio/wav")},
            data={"model": _TEST_MODEL, "response_format": "verbose_json", "device": "GPU"},
        )
        assert response.status_code == 200, f"Unexpected status: {response.status_code} {response.text}"
        body = response.json()
        assert "text" in body
        assert "segments" in body
        assert "duration" in body
        assert body["duration"] > 0

    def test_transcribe_json_format(self, http_client: httpx.Client, test_wav_path: str):
        """POST /v1/audio/transcriptions with response_format=json returns only text."""
        with open(test_wav_path, "rb") as f:
            audio_bytes = f.read()

        response = http_client.post(
            "/v1/audio/transcriptions",
            files={"file": ("audio.wav", audio_bytes, "audio/wav")},
            data={"model": _TEST_MODEL, "response_format": "json", "device": "GPU"},
        )
        assert response.status_code == 200
        body = response.json()
        assert "text" in body

    def test_transcribe_fixed_language(self, http_client: httpx.Client, test_wav_path: str):
        """POST /transcribe with language_mode=Fixed sets detected_language."""
        response = http_client.post(
            "/transcribe",
            json={
                "audio_path": test_wav_path,
                "model_path": _get_model_path(http_client),
                "device": "GPU",
                "language_mode": "Fixed",
                "language_code": "en",
            },
        )
        assert response.status_code == 200
        body = response.json()
        assert body.get("detected_language") == "en"

    def test_transcribe_with_speech(self, http_client: httpx.Client, test_wav_path: str, has_speech_audio: bool):
        """When real speech audio is provided, plain_text and segments must be non-empty.

        Set SIDECAR_TEST_AUDIO_PATH to a real audio file (WAV or OGG) to activate this test.
        Example:
            SIDECAR_TEST_AUDIO_PATH=tests/fixtures/sample.ogg pytest tests/ -m gpu -v
        """
        if not has_speech_audio:
            pytest.skip("Set SIDECAR_TEST_AUDIO_PATH to a real speech audio file to run this test")

        with open(test_wav_path, "rb") as f:
            audio_bytes = f.read()

        response = http_client.post(
            "/v1/audio/transcriptions",
            files={"file": ("audio.wav", audio_bytes, "audio/wav")},
            data={"model": _TEST_MODEL, "response_format": "verbose_json", "device": "GPU"},
        )
        assert response.status_code == 200, f"Unexpected status: {response.status_code} {response.text}"
        body = response.json()
        assert body["text"].strip(), (
            "Expected non-empty transcription text for real speech audio. "
            "Check that the audio file contains intelligible speech."
        )
        assert len(body["segments"]) > 0, "Expected at least one transcript segment for real speech audio"

    def test_pipeline_cache(self, http_client: httpx.Client, test_wav_path: str):
        """Repeated transcription with the same model should not re-load the pipeline."""
        model_path = _get_model_path(http_client)
        payload = {
            "audio_path": test_wav_path,
            "model_path": model_path,
            "device": "GPU",
            "language_mode": "Auto",
        }

        # First call (model may already be cached by earlier tests in this session)
        t0 = time.monotonic()
        r1 = http_client.post("/transcribe", json=payload)
        first_elapsed = time.monotonic() - t0
        assert r1.status_code == 200

        # Second call (model must be cached)
        t1 = time.monotonic()
        r2 = http_client.post("/transcribe", json=payload)
        second_elapsed = time.monotonic() - t1
        assert r2.status_code == 200

        # The second call must not be significantly slower than the first.
        # A generous 3-second buffer handles timing noise while catching pipeline re-loads
        # (which would add tens of seconds of overhead).
        assert second_elapsed < first_elapsed + 3.0, (
            f"Expected second call ({second_elapsed:.2f}s) not significantly slower than "
            f"first ({first_elapsed:.2f}s). Pipeline may be reloaded on every call."
        )


@pytest.mark.gpu
class TestOpenAiModels:
    def test_v1_models_includes_installed(self, http_client: httpx.Client):
        """GET /v1/models returns the installed model in OpenAI format."""
        response = http_client.get("/v1/models")
        assert response.status_code == 200
        body = response.json()
        assert body["object"] == "list"
        ids = [m["id"] for m in body["data"]]
        assert _TEST_MODEL in ids, f"Expected {_TEST_MODEL} in /v1/models. Got: {ids}"


@pytest.mark.gpu
class TestModelDelete:
    def test_model_delete(self, http_client: httpx.Client):
        """DELETE /models/{model} returns 204; GET /models then shows is_installed=false.

        This test requires the model to be installed but NOT currently loaded in the
        pipeline cache. If transcription tests run in the same session they load the
        model, which causes DELETE to correctly return 409. In that case the test is
        skipped — run it in isolation with a fresh sidecar to validate the full flow:
            pytest tests/test_sidecar_e2e.py::TestModelDelete::test_model_delete -m gpu -v
        """
        # Ensure model is installed first
        models_before = {m["name"]: m for m in http_client.get("/models").json()["models"]}
        if not models_before.get(_TEST_MODEL, {}).get("is_installed"):
            pytest.skip(f"Model {_TEST_MODEL} not installed, skipping delete test")

        response = http_client.delete(f"/models/{_TEST_MODEL}")
        if response.status_code == 409:
            pytest.skip(
                f"Model {_TEST_MODEL} is loaded in the pipeline cache (409). "
                "This is correct behaviour. Run this test in isolation before any "
                "transcription tests to validate the delete flow."
            )
        assert response.status_code == 204, f"Expected 204, got {response.status_code}: {response.text}"

        models_after = {m["name"]: m for m in http_client.get("/models").json()["models"]}
        assert not models_after[_TEST_MODEL]["is_installed"], (
            f"Expected {_TEST_MODEL} to be uninstalled after delete"
        )

    def test_delete_unknown_model_returns_404(self, http_client: httpx.Client):
        """DELETE /models/nonexistent-model returns 404."""
        response = http_client.delete("/models/nonexistent-model-xyz")
        assert response.status_code == 404

    def test_download_unknown_model_returns_404(self, http_client: httpx.Client):
        """POST /models/download with unknown model returns 404."""
        response = http_client.post("/models/download", json={"model": "nonexistent-model-xyz"})
        assert response.status_code == 404


# ---------------------------------------------------------------------------
# Helper
# ---------------------------------------------------------------------------

def _get_model_path(client: httpx.Client) -> str:
    """Return install_path for _TEST_MODEL, or raise if not installed."""
    response = client.get("/models")
    assert response.status_code == 200
    models = {m["name"]: m for m in response.json()["models"]}
    entry = models.get(_TEST_MODEL)
    if not entry or not entry["is_installed"]:
        pytest.skip(f"Model {_TEST_MODEL} is not installed. Run test_model_download_sse first.")
    return entry["install_path"]
