# app_restatify-support-chat

Cross-platform support client using Uno Platform.

## Scope (initial)

- iOS and Android mobile support workflow
- Windows and Linux desktop support workflow
- WebAssembly target for browser access

## Current Scaffold

The runnable Uno solution is located in:

- `src/SupportChat.App/Restatify.SupportChat.slnx`

Additional folders reserved for planned package split:

- `src/SupportChat.Core`
- `src/SupportChat.Infrastructure`
- `src/SupportChat.Contracts`

## Current Features

- Login with WordPress credentials and support API key session flow
- Realtime support updates via WebSocket with polling fallback
- Conversation details actions for AI mode, booking trigger, and delete
- Mobile-focused UX updates for Android and Windows/Desktop workflows

## Build (from repo root)

```bash
dotnet restore src/SupportChat.App/Restatify.SupportChat.slnx
dotnet build src/SupportChat.App/Restatify.SupportChat.slnx
```

## Notes

- Scaffold generated with Uno.Extensions template targeting `net10.0`.
- Architecture split into dedicated class libraries will be introduced incrementally.
- Support chat auth is WordPress-specific: sign in first, generate a support API key, then use that key for chat requests and live updates.

## Release Notes

### v1.1.0

- Improved Android runtime stability (network handler and foreground-service permissions).
- Added clearer login status messaging and setup navigation from login screen.
- Refined smartphone conversation UI layout, send actions, and autoscroll behavior.
- Enforced WebSocket-first updates with polling used only as fallback.
- Added handling for realtime conversation deletion events.
