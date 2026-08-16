# TranscriptLab Nova Design System

## 1. Visual language

TranscriptLab Nova is a calm, operational desktop-first interface built with Material UI. Surfaces are light, compact, and information-forward. New UI should reuse existing MUI components and theme tokens rather than introduce a parallel visual language.

## 2. Color tokens

- Primary: `#2b5fb8`; light `#6d95dd`; dark `#1f447f`.
- Secondary: `#3aa0c8`; light `#7dcbe4`; dark `#236d8f`.
- Background: `#f3f4f6`; paper: `#ffffff`.
- Text: primary `#1c1f23`; secondary `#5f6368`.
- Semantic colors use the MUI `success`, `warning`, `error`, and `info` palette entries in `src/frontend/src/theme.ts`.
- Dividers use the theme divider token; do not add page-specific neutral borders.

## 3. Typography

Roboto is the primary family with Helvetica and Arial fallbacks. Headings use medium weight; subtitles use 600; buttons remain sentence case. Supporting explanations use `body2` or `caption` with `text.secondary`.

## 4. Spacing and shape

Use the MUI 8 px spacing scale. Standard card padding is 16–24 px, page-section gaps are 16–24 px, and compact inline gaps are 8–12 px. The global shape radius is 6 px; buttons use 7 px.

## 5. Layout

Pages live inside the existing application shell and begin with `TopBar`. Operational metadata uses wrapping chips or compact two-column grids. Settings use outlined `Paper` sections and collapse to one column on small screens. Respect safe-area variables in full-height drawers.

## 6. Components

- Use outlined `Paper` for grouped content and `Accordion` for secondary diagnostics.
- Use `Alert` for disclosures that affect data transfer, availability, or cost.
- Use labeled `Switch` controls for boolean processing settings and keep explanatory copy adjacent.
- For diarization, show an explicit Local mode / Provider mode / xAI timing source selector. Offer Provider mode only when the selected engine and model advertise native support; offer xAI timing only for the compatible verified OpenRouter word-timestamp models when direct xAI is configured; reveal Basic/Improved tuning only for Local mode.
- Settings is a three-tab surface named exactly `Settings`, `Local Model Manager`, and `System Capabilities`. Keep Save and Reset in `Settings`; lazy-load the catalog and capability panels after first selection while retaining visited-panel state. Diagnostics remains outside those tabs as its unchanged separate route.
- System-capability cards show only sanitized provider, compute, and CPU-summary fields. Do not surface API keys, URLs, paths, raw errors, or private device identifiers.
- On project details, combine timing and hosted-usage evidence in one collapsed-by-default Processing Details accordion. Group information by STT engine/model, local processing, and speaker-role attribution, and label each group Local, Hosted, or Not used.
- Use `Chip` for short status and metadata values, not explanatory prose.
- Reuse `EmptyState`, `ProjectStatusChip`, and existing shell components.

## 7. Motion and feedback

Rely on MUI defaults. Loading uses skeletons, linear progress, or existing button disabled states. Avoid decorative animation.

## 8. Accessibility and responsive behavior

Controls require visible labels or `aria-label` text. Maintain WCAG AA contrast through theme colors. At 375 px, grids become one column and actions may wrap or stretch. At 768 px and above, metadata and settings can use two columns. Do not rely on color alone to convey estimated versus actual cost or availability.
