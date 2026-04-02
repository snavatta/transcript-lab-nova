# AGENTS.md

This repository is designed to be built primarily by AI coding agents.

The markdown documents in this repository are not optional reference material. They are the implementation spec. An agent working in this repo must read the relevant documents before writing code and must keep code changes aligned with them.

## Public Repository Guard

This repository is intended to be hosted on a public GitHub repository and developed as open source.

Agents must act accordingly:
- never commit secrets, tokens, passwords, API keys, private certificates, or real credentials
- never add private local network addresses, private hostnames, personal filesystem paths, or deployment-specific internal URLs unless clearly documented as placeholders
- never copy proprietary code, closed-source assets, or restricted documentation into the repository
- prefer clearly fake example values such as `example.com`, `/data`, `YOUR_API_KEY`, or `changeme`
- treat logs, screenshots, samples, fixtures, and test data as publishable artifacts and sanitize them before adding them
- flag any suspected secret, credential, or sensitive data exposure immediately instead of normalizing it
- when reviewing code or docs, check for accidental data leakage as a first-class concern

## Repository Documents

### `class-transcriber-shared-api-contract.md`
Use for:
- API routes
- request/response DTOs
- enum values
- status transitions
- upload payload shape
- export query parameters
- frontend/backend contract alignment

This is the source of truth for:
- route shapes
- DTO field names
- API payload structure
- wire-level contract behavior

Read this file:
- before implementing any frontend API client code
- before implementing any backend endpoint
- before changing project statuses, DTOs, or route parameters
- before adding any feature that crosses frontend/backend boundaries

### `class-transcriber-frontend-prd.md`
Use for:
- frontend behavior
- page requirements
- UX flows
- component expectations
- what the user must be able to do in the UI

This is the source of truth for:
- frontend product behavior
- page content
- user flows
- UI-visible requirements

Read this file:
- before implementing any frontend page, component, or flow
- before changing navigation, upload UX, project detail UX, queue UX, export UX, or settings UX
- before removing a UI element that may be required by the PRD

### `class-transcriber-frontend-tech-stack-requirements.md`
Use for:
- frontend framework/library choices
- frontend testing rules
- frontend routing/state/styling decisions
- browser/API usage standards

This is the source of truth for:
- frontend library choices
- frontend architectural constraints
- frontend testing/tooling expectations

Read this file:
- before selecting a new frontend dependency
- before changing how routing, forms, validation, data fetching, styling, testing, or media playback work
- before introducing a frontend pattern not already established

### `class-transcriber-backend-prd.md`
Use for:
- backend product behavior
- processing model
- storage behavior
- queue/job lifecycle
- backend functional scope

This is the source of truth for:
- backend domain behavior
- processing workflow
- entity-level requirements
- backend acceptance criteria

Read this file:
- before implementing backend entities, services, queue logic, uploads, transcription flow, exports, or storage accounting
- before changing retry, cancellation, file handling, or processing behavior

### `class-transcriber-backend-tech-stack-requirements.md`
Use for:
- backend runtime/framework/library choices
- persistence/logging/testing standards
- homelab hardware assumptions
- background processing tool policy

This is the source of truth for:
- backend technology choices
- backend infrastructure constraints
- backend testing/tooling standards
- operational assumptions for the target host

Read this file:
- before selecting backend libraries or infrastructure patterns
- before introducing background job frameworks, new persistence layers, new schedulers, or external infrastructure
- before changing concurrency, runtime configuration, or observability patterns

## Required Read Order

### For frontend-only work
1. `class-transcriber-frontend-prd.md`
2. `class-transcriber-shared-api-contract.md`
3. `class-transcriber-frontend-tech-stack-requirements.md`

### For backend-only work
1. `class-transcriber-backend-prd.md`
2. `class-transcriber-shared-api-contract.md`
3. `class-transcriber-backend-tech-stack-requirements.md`

### For full-stack or cross-cutting work
1. `class-transcriber-shared-api-contract.md`
2. `class-transcriber-frontend-prd.md`
3. `class-transcriber-backend-prd.md`
4. `class-transcriber-frontend-tech-stack-requirements.md`
5. `class-transcriber-backend-tech-stack-requirements.md`

## Conflict Resolution

If documents appear to conflict, use this precedence:

1. `class-transcriber-shared-api-contract.md` for routes, DTOs, request shapes, response shapes, enums, and API-visible behavior
2. PRD files for product behavior and user-visible requirements
3. tech stack requirement files for library/framework/runtime/tooling choices

If there is still a conflict:
- do not silently guess
- update the conflicting documents so they align
- keep the code and docs consistent in the same change when feasible

## Agent Operating Rules

- Do not invent routes, DTO fields, statuses, or query parameters that are not supported by the shared contract unless you also update the contract.
- Do not swap frontend or backend libraries without updating the relevant tech stack requirements file.
- Do not remove required product behavior because it seems inconvenient to implement.
- Do not treat PRDs as aspirational. Treat them as implementation requirements unless explicitly marked optional, future, nice-to-have, or non-MVP.
- Keep frontend, backend, and shared contract changes synchronized. A cross-boundary change is incomplete if only one side is updated.
- Prefer the simplest implementation that satisfies the documented requirements and homelab constraints.

## Ask Before Changing

An agent must stop and ask before changing:
- the open-source license
- the public repository name or product name
- the shared API contract in a way that breaks existing DTOs or route shapes
- the approved frontend or backend stack
- storage deletion semantics that could cause data loss
- concurrency defaults beyond the documented homelab assumptions
- security scope such as adding auth, external exposure assumptions, or cloud dependencies

## When To Update Docs

An agent must update the relevant markdown files when it changes:
- an API route
- a DTO shape
- an enum or status
- an upload payload or export parameter
- a required user flow
- a required backend behavior
- an approved tech choice
- operational assumptions such as concurrency, storage accounting, or hardware-sensitive behavior

## Implementation Guidance

## Subagents And Delegation

Subagents are allowed in this repository when they are properly coordinated.

Rules:
- delegate only well-scoped tasks with a clear output
- assign explicit ownership for files or responsibility areas before parallel work starts
- do not assign overlapping write scopes to multiple agents unless one agent is clearly integrating the other work
- require every subagent to read the relevant repo documents for its scope before making changes
- require every subagent to follow the shared API contract and relevant tech stack requirements
- synchronize cross-boundary changes so frontend, backend, and contract updates land together
- do not use subagents to bypass the public-repository guard, documentation-update rules, or ask-before-changing rules
- after delegated work returns, review and integrate it instead of assuming it is correct by default

### Frontend agent
- implement the UI required by the frontend PRD
- consume only the shared API contract
- follow the frontend tech stack standard exactly

### Backend agent
- implement the backend behavior required by the backend PRD
- expose only the API documented in the shared contract
- follow the backend tech stack standard exactly

### Full-stack agent
- start from the shared API contract
- verify both PRDs before implementation
- update all affected documents if a cross-boundary change is necessary

## Definition Of Done

A task is not done unless:
- code matches the relevant PRD
- API behavior matches the shared contract
- technology choices match the tech stack requirements
- tests cover the changed critical behavior when appropriate
- affected docs are updated if the implementation changed the agreed design
