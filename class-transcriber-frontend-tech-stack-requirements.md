
# Frontend Tech Stack Requirements

## Project Baseline
- **Node.js 22 LTS** - Required local and CI runtime
- **npm** - Standard package manager
- **React 19** - Required frontend runtime
- **TypeScript strict mode** - Must remain enabled for all application code
- **Evergreen browser support** - Target current Chrome, Firefox, Safari, and Edge releases

## Core Framework
- **React** - UI library for building component-based interfaces
- **TypeScript** - Static typing for JavaScript

## Form Management
- **react-hook-form** - Performant form handling with minimal re-renders
- **@hookform/resolvers** - Validation schema integration

## Validation
- **Zod** - Schema validation and type inference

## State Management
- **SWR** - Server state management and data fetching
- **React Context** - Shared client state for lightweight application needs
- **Zustand** - Approved client state library if app complexity proves React Context insufficient
- Use **React Context** by default for auth state, theme state, and other low-frequency shared UI state
- Introduce **Zustand** only when shared client state requires one or more of the following:
  - frequent updates across distant branches of the tree
  - selector-based subscriptions to avoid broad re-renders
  - cross-page workflows that become difficult to maintain with nested providers and reducer/context patterns

## UI Framework and Styling
- **Material UI (MUI)** - Primary component library and design system foundation
- **MUI Icons** - Standard icon set for navigation, actions, and status indicators
- **MUI System** - Primary styling approach for component-level customization
- **Global theme configuration** - Centralized tokens for color, typography, spacing, and component overrides
- New UI should prefer MUI components and theme tokens over custom one-off styling

## HTTP Client
- **Fetch API** - Standard API communication layer
- Wrap `fetch` in shared helpers for base URL handling, auth headers, and error normalization
- Application code should not call `fetch` directly unless there is a documented exception

## Routing
- **wouter** - Lightweight client-side routing

## Media and Browser APIs
- **Native HTML audio/video elements** - Default approach for media playback
- Use browser-native file selection, drag-and-drop, and `FormData` for upload flows unless a clear limitation requires a helper library
- Prefer browser-native download behavior and backend-generated export files for PDF, Markdown, TXT, and HTML exports

## Development Tools
- **Vite** - Fast build tool and dev server
- **ESLint** - Code linting
- **Prettier** - Code formatting

## Testing
- **Vitest** - Unit testing framework
- **React Testing Library** - Component testing utilities
- **Playwright** - End-to-end testing and validation of user flows
- Critical user flows must be covered by Playwright tests before release
- API mocking strategy should support deterministic tests in both Vitest and Playwright

## Additional Libraries
- **date-fns** - Date manipulation
- **clsx** - Conditional className utilities
