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

## Build (from repo root)

```bash
dotnet restore src/SupportChat.App/Restatify.SupportChat.slnx
dotnet build src/SupportChat.App/Restatify.SupportChat.slnx
```

## Notes

- Scaffold generated with Uno.Extensions template targeting `net10.0`.
- Architecture split into dedicated class libraries will be introduced incrementally.
- Support chat auth is WordPress-specific: sign in first, generate a support API key, then use that key for chat requests and live updates.
