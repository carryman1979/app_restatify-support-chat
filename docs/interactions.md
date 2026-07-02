# Interactions

## Runtime behavior

- Live updates are the primary mechanism when available.
- Polling is fallback-only behavior when live updates are unavailable.
- Login should lead into the shell and then into the main conversation surface.
- The app treats a stored API key as a restored session; when present, the main model starts logged in.
- Live updates connect through WebSocket updates on `/v1/support/ws/updates` using the stored API key.

## Authentication flow

- The login screen validates the configured base URL before attempting sign-in.
- Username/password authenticate against the WordPress support API.
- A successful login returns bearer-token material and then a second call generates the stored support API key.
- If no API key is stored, the app should not silently assume authenticated support access except for the explicit local-dev fallback.

## Status behavior

- Show clear status when live updates disconnect.
- Keep the fallback state visible when polling is active.
- Avoid silent failures during startup and reconnect.

## Android notes

- Splash screen behavior must resolve quickly.
- Emulator startup and deployment are part of the debug loop.
- If Android gets stuck on a blank screen, check startup and theme resources first.

## Chat API notes

- Conversation fetches, replies, tools, AI mode changes, delete actions, and booking overlay actions all go through the support API client.
- Connection errors are retried once before being surfaced as support API failures.
- The client should prefer clear operator-facing errors over silent retries when the WordPress plugin or container is unavailable.
