#!/bin/bash
# End-to-end test: starts the API, uploads a test audio file, queues it, and waits for transcription.
# Usage: ./e2e-test.sh [audio_file]
# If no audio_file is provided, generates a short test WAV using ffmpeg.

set -eu

API_BASE="http://localhost:5000"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
API_DIR="$SCRIPT_DIR/src/ClassTranscriber.Api"
TEST_AUDIO="${1:-}"
TIMEOUT_SECS=180
POLL_INTERVAL=3

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

pass() { echo -e "${GREEN}PASS${NC}: $1"; }
fail() { echo -e "${RED}FAIL${NC}: $1"; exit 1; }
info() { echo -e "${YELLOW}INFO${NC}: $1"; }

cleanup() {
    if [[ -n "${API_PID:-}" ]]; then
        info "Stopping API server (PID $API_PID)..."
        kill "$API_PID" 2>/dev/null || true
        wait "$API_PID" 2>/dev/null || true
    fi
    if [[ -n "${GENERATED_AUDIO:-}" && -f "$GENERATED_AUDIO" ]]; then
        rm -f "$GENERATED_AUDIO"
    fi
}
trap cleanup EXIT

# --- Step 1: Generate test audio if needed ---
if [[ -z "$TEST_AUDIO" ]]; then
    GENERATED_AUDIO="/tmp/e2e-test-audio-$$.wav"
    info "Generating 3-second test audio file..."
    ffmpeg -y -f lavfi -i "sine=frequency=440:duration=3" -ar 16000 -ac 1 "$GENERATED_AUDIO" 2>/dev/null
    TEST_AUDIO="$GENERATED_AUDIO"
fi

if [[ ! -f "$TEST_AUDIO" ]]; then
    fail "Audio file not found: $TEST_AUDIO"
fi
pass "Test audio file ready: $TEST_AUDIO ($(du -h "$TEST_AUDIO" | cut -f1))"

# --- Step 2: Check dependencies ---
info "Checking dependencies..."
command -v whisper-cli >/dev/null 2>&1 || fail "whisper-cli not found in PATH"
pass "whisper-cli available"

command -v ffmpeg >/dev/null 2>&1 || fail "ffmpeg not found in PATH"
pass "ffmpeg available"

MODEL_PATH="$API_DIR/data/models/ggml-small.bin"
[[ -f "$MODEL_PATH" ]] || fail "Model file not found: $MODEL_PATH"
pass "Model file exists ($(du -h "$MODEL_PATH" | cut -f1))"

# --- Step 3: Start API server ---
info "Starting API server..."
cd "$API_DIR"
dotnet run --urls "$API_BASE" > /tmp/e2e-api.log 2>&1 &
API_PID=$!

# Wait for server to be ready
info "Waiting for API to be ready..."
for i in $(seq 1 60); do
    if curl -s "$API_BASE/api/health" >/dev/null 2>&1; then
        break
    fi
    sleep 1
done
curl -s "$API_BASE/api/health" >/dev/null 2>&1 || fail "API server failed to start within 60s"
pass "API server running on $API_BASE"

# --- Step 4: Create a folder ---
info "Creating test folder..."
FOLDER_RESP=$(curl -s -X POST "$API_BASE/api/folders" \
    -H "Content-Type: application/json" \
    -d '{"name":"E2E Test Folder"}')

FOLDER_ID=$(echo "$FOLDER_RESP" | python3 -c "import sys,json; print(json.load(sys.stdin)['id'])" 2>/dev/null)
[[ -n "$FOLDER_ID" ]] || fail "Failed to create folder. Response: $FOLDER_RESP"
pass "Folder created: $FOLDER_ID"

# --- Step 5: Upload audio file with autoQueue ---
info "Uploading audio file with autoQueue=true..."
UPLOAD_RESP=$(curl -s -X POST "$API_BASE/api/uploads/batch" \
    -F "folderId=$FOLDER_ID" \
    -F "autoQueue=true" \
    -F "files=@$TEST_AUDIO")

PROJECT_ID=$(echo "$UPLOAD_RESP" | python3 -c "import sys,json; print(json.load(sys.stdin)['createdProjects'][0]['id'])" 2>/dev/null)
[[ -n "$PROJECT_ID" ]] || fail "Failed to upload file. Response: $UPLOAD_RESP"
pass "Project created and queued: $PROJECT_ID"

# --- Step 6: Poll for completion ---
info "Waiting for transcription to complete (timeout: ${TIMEOUT_SECS}s)..."
ELAPSED=0
LAST_STATUS=""

while [[ $ELAPSED -lt $TIMEOUT_SECS ]]; do
    PROJECT_RESP=$(curl -s "$API_BASE/api/projects/$PROJECT_ID" || echo "{}")
    STATUS=$(echo "$PROJECT_RESP" | python3 -c "import sys,json; print(json.load(sys.stdin).get('status','unknown'))" 2>/dev/null || echo "unknown")
    PROGRESS=$(echo "$PROJECT_RESP" | python3 -c "import sys,json; print(json.load(sys.stdin).get('progress', 0))" 2>/dev/null || echo "0")

    if [[ "$STATUS" != "$LAST_STATUS" ]]; then
        info "Status: $STATUS (progress: $PROGRESS%)"
        LAST_STATUS="$STATUS"
    fi

    if [[ "$STATUS" == "Completed" ]]; then
        pass "Transcription completed!"
        break
    fi

    if [[ "$STATUS" == "Failed" ]]; then
        ERROR_MSG=$(echo "$PROJECT_RESP" | python3 -c "import sys,json; print(json.load(sys.stdin).get('errorMessage', 'unknown'))" 2>/dev/null)
        fail "Transcription failed: $ERROR_MSG"
    fi

    sleep $POLL_INTERVAL
    ELAPSED=$((ELAPSED + POLL_INTERVAL))
done

if [[ "$STATUS" != "Completed" ]]; then
    fail "Transcription timed out after ${TIMEOUT_SECS}s (last status: $STATUS)"
fi

# --- Step 7: Verify transcript ---
info "Verifying transcript..."
TRANSCRIPT_RESP=$(curl -s "$API_BASE/api/projects/$PROJECT_ID/transcript")
TRANSCRIPT_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$API_BASE/api/projects/$PROJECT_ID/transcript")

if [[ "$TRANSCRIPT_STATUS" != "200" ]]; then
    fail "Transcript endpoint returned HTTP $TRANSCRIPT_STATUS"
fi

PLAIN_TEXT=$(echo "$TRANSCRIPT_RESP" | python3 -c "import sys,json; print(json.load(sys.stdin).get('plainText', ''))" 2>/dev/null)
SEGMENT_COUNT=$(echo "$TRANSCRIPT_RESP" | python3 -c "import sys,json; print(len(json.load(sys.stdin).get('segments', [])))" 2>/dev/null)

if [[ -z "$PLAIN_TEXT" ]]; then
    fail "Transcript has empty plainText"
fi
pass "Transcript text: \"${PLAIN_TEXT:0:100}...\""
pass "Segment count: $SEGMENT_COUNT"

# --- Step 8: Verify project detail ---
PROJECT_DETAIL=$(curl -s "$API_BASE/api/projects/$PROJECT_ID")
FINAL_STATUS=$(echo "$PROJECT_DETAIL" | python3 -c "import sys,json; print(json.load(sys.stdin)['status'])" 2>/dev/null)
DURATION=$(echo "$PROJECT_DETAIL" | python3 -c "import sys,json; print(json.load(sys.stdin).get('durationMs', 0))" 2>/dev/null)

pass "Final status: $FINAL_STATUS"
pass "Duration: ${DURATION}ms"

# --- Step 9: Test export ---
info "Testing text export..."
EXPORT_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$API_BASE/api/projects/$PROJECT_ID/export?format=txt")
if [[ "$EXPORT_STATUS" == "200" ]]; then
    pass "Text export works (HTTP 200)"
else
    fail "Text export returned HTTP $EXPORT_STATUS"
fi

# --- Step 10: Cleanup ---
info "Cleaning up test data..."
curl -s -X DELETE "$API_BASE/api/projects/$PROJECT_ID" >/dev/null
curl -s -X DELETE "$API_BASE/api/folders/$FOLDER_ID" >/dev/null
pass "Test data cleaned up"

echo ""
echo -e "${GREEN}=== ALL E2E TESTS PASSED ===${NC}"
