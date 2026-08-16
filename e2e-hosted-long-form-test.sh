#!/usr/bin/env bash
set -euo pipefail

optional=false
allow_paid=false
for argument in "$@"; do
  case "$argument" in
    --optional) optional=true ;;
    --allow-paid) allow_paid=true ;;
    *) echo "Unknown argument: $argument" >&2; exit 2 ;;
  esac
done

media_path="${HOSTED_LONG_FORM_MEDIA:-}"
if [[ -z "${OPENROUTER_API_KEY:-}" || -z "${XAI_API_KEY:-}" || -z "$media_path" || ! -f "$media_path" ]]; then
  if [[ "$optional" == true ]]; then
    echo "SKIPPED: missing credentials/media"
    exit 0
  fi
  echo "Missing OPENROUTER_API_KEY, XAI_API_KEY, or readable HOSTED_LONG_FORM_MEDIA." >&2
  exit 2
fi

if [[ "$allow_paid" != true ]]; then
  echo "Refusing paid provider calls without --allow-paid." >&2
  exit 2
fi

case "${media_path,,}" in
  *.flac) ;;
  *) echo "HOSTED_LONG_FORM_MEDIA must be a FLAC file." >&2; exit 2 ;;
esac

base_url="${TRANSCRIPTLAB_BASE_URL:-http://127.0.0.1:5180}"
timeout_seconds="${HOSTED_LONG_FORM_TIMEOUT_SECONDS:-3600}"
curl --fail --silent --show-error "$base_url/api/health" >/dev/null

folder_id="$(curl --fail --silent --show-error \
  -H 'Content-Type: application/json' \
  -d '{"name":"Hosted long-form smoke"}' \
  "$base_url/api/folders" | jq -er '.id')"
settings='{"engine":"OpenRouter","model":"openai/whisper-large-v3","languageMode":"Auto","languageCode":null,"audioNormalizationEnabled":true,"diarizationEnabled":true,"diarizationSource":"Xai","diarizationMode":"Basic","speakerRoleAttributionEnabled":false}'
project_id="$(curl --fail --silent --show-error \
  -F "folderId=$folder_id" \
  -F 'autoQueue=true' \
  -F "settings=$settings" \
  -F "files=@$media_path;type=audio/flac" \
  "$base_url/api/uploads/batch" | jq -er '.createdProjects[0].id')"

deadline=$((SECONDS + timeout_seconds))
while (( SECONDS < deadline )); do
  project="$(curl --fail --silent --show-error "$base_url/api/projects/$project_id")"
  status="$(jq -er '.status' <<<"$project")"
  case "$status" in
    Completed)
      curl --fail --silent --show-error "$base_url/api/projects/$project_id/transcript" \
        | jq '{status:"Completed", segmentCount, hostedProcessing:{sttProvider:.hostedProcessing.sttProvider,requestCount:.hostedProcessing.requestCount,diarizationSource:.hostedProcessing.diarizationSource,diarizationRequestCount:.hostedProcessing.diarizationRequestCount,totalContainsEstimate:.hostedProcessing.totalContainsEstimate}}'
      exit 0
      ;;
    Failed|Cancelled)
      jq -n --arg status "$status" '{status:$status}' >&2
      exit 1
      ;;
  esac
  sleep 5
done

echo "Hosted long-form smoke timed out." >&2
exit 1
