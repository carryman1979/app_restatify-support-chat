# UX Flows

## Primary flow

1. Start app.
2. Authenticate.
3. Enter shell.
4. Open conversation list.
5. Open conversation details.
6. Receive live updates or fall back to polling.

## Authentication flow

1. Enter base URL if needed.
2. Sign in with WordPress credentials.
3. Generate and store support API key.
4. Restore the main shell with the authenticated session.

## Secondary flow

- Start app on Android emulator.
- Verify splash.
- Confirm shell and login render.
- Confirm navigation to main content after login.

## Failure flow

- Live updates fail.
- Show a clear disconnected state.
- Switch to polling.
- Reconnect automatically when possible.

## Chat API failure flow

- Missing or invalid API key should block support actions until the user signs in again.
- A transient plugin or backend outage should retry once and then surface an understandable error.
- On localhost development, the fallback key should keep the developer loop moving when appropriate.
