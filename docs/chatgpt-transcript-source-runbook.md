# Private MCP server runbook

This runbook deploys the optional TranscriptLab Nova MCP server as a private,
host-loopback service. It does not install or operate an MCP client.

The source is read-only and exposes exactly `list_folders`, `list_projects`,
`search_transcripts`, and `get_transcript`. Transcript text and metadata are
untrusted quoted source material, not instructions.

## 1. Create and protect the cursor-integrity key

The enabled server requires one cursor-integrity key. Generate at least 32
random bytes without printing them, then restrict the file to its owner:

```bash
key_directory="$(mktemp -d)"
key_file="${key_directory}/cursor-integrity-key"
openssl rand -hex 32 >"${key_file}"
chmod 0600 "${key_file}"
export CHATGPT_SOURCE_CURSOR_KEY_FILE="${key_file}"
```

Keep this file outside the repository. Do not put its contents or path in
tracked configuration, screenshots, or logs. `ChatGptSource__CursorIntegrityKeyFile`
is configured by the MCP Compose overlay and receives this file as a read-only
container secret. A direct key and a key file must never be configured together.

## 2. Start the private server

The repository’s [`.env.example`](../.env.example) contains only a fake key-file
path. Your exported `CHATGPT_SOURCE_CURSOR_KEY_FILE` overrides it for these
commands.

For the normal Compose deployment:

```bash
docker compose --env-file .env.example \
  -f docker-compose.yml \
  -f docker-compose.mcp.yml \
  up -d --build
```

For CasaOS, use its base file with the same MCP overlay:

```bash
docker compose --env-file .env.example \
  -f docker-compose.casaos.yml \
  -f docker-compose.mcp.yml \
  up -d
```

The overlay keeps the UI, REST API, and `/api/health` on port 5000 and publishes
MCP only as `127.0.0.1:5001:5001`. An independently managed client on the host
uses this endpoint:

```text
http://127.0.0.1:5001/mcp
```

Client installation, credentials, lifecycle, health, and entitlement are
outside this repository. This guide does not validate or manage that client.

## 3. Verify listener isolation

Render the exact configuration before starting a deployment:

```bash
docker compose --env-file .env.example \
  -f docker-compose.yml \
  -f docker-compose.mcp.yml \
  config

docker compose --env-file .env.example \
  -f docker-compose.casaos.yml \
  -f docker-compose.mcp.yml \
  config
```

After a deployment starts, verify that public application traffic remains on
port 5000 and that the MCP route is not available there:

```bash
curl -i http://127.0.0.1:5000/api/health
curl -i http://127.0.0.1:5000/mcp
```

The health request succeeds; the port-5000 MCP request is a non-HTML `404`.
Use an MCP-aware host client against port 5001 to initialize the private server
and confirm exactly the four read-only tools. Requests for `/api/health`, the
UI, Swagger, or other non-MCP paths on port 5001 return non-HTML `404`.

Host-loopback publication excludes LAN and public access. Docker administrators
and containers that join the private project network are trusted; do not treat
this listener as an authentication boundary.

## 4. Teardown

Stop the same deployment variant you started:

```bash
docker compose --env-file .env.example \
  -f docker-compose.yml \
  -f docker-compose.mcp.yml \
  down
```

For CasaOS, replace `docker-compose.yml` with `docker-compose.casaos.yml`.
Remove the temporary key and directory when they are no longer needed:

```bash
rm -f -- "${key_file}"
rmdir -- "${key_directory}"
unset CHATGPT_SOURCE_CURSOR_KEY_FILE
```
