#!/usr/bin/env bash
set -euo pipefail

repository_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
maximum_lines=250
files=(
  src/ClassTranscriber.Api/Mcp/ChatGptSourceContentModels.cs
  src/ClassTranscriber.Api/Mcp/ChatGptSourceContentService.cs
  src/ClassTranscriber.Api/Mcp/TranscriptContentPageBuilder.cs
  src/ClassTranscriber.Api/Mcp/TranscriptCursorCodec.cs
  src/ClassTranscriber.Api/Mcp/TranscriptOccurrenceMatcher.cs
)

status=0
for relative_path in "${files[@]}"; do
  absolute_path="${repository_root}/${relative_path}"
  if [[ ! -f "${absolute_path}" ]]; then
    printf 'FAIL %s missing\n' "${relative_path}"
    status=1
    continue
  fi

  pure_lines=$(awk '
    /^[[:space:]]*$/ { next }
    /^[[:space:]]*\/\// { next }
    /^[[:space:]]*\/\*/ { in_block = 1; next }
    in_block && /\*\// { in_block = 0; next }
    in_block { next }
    { count++ }
    END { print count + 0 }
  ' "${absolute_path}")
  if (( pure_lines > maximum_lines )); then
    printf 'FAIL %s %d/%d pure lines\n' "${relative_path}" "${pure_lines}" "${maximum_lines}"
    status=1
  else
    printf 'PASS %s %d/%d pure lines\n' "${relative_path}" "${pure_lines}" "${maximum_lines}"
  fi
done

exit "${status}"
