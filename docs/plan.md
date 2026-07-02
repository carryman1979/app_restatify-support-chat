# Plan

## Current priorities

- Keep Android startup aligned with the working baseline.
- Preserve the login -> shell -> main navigation path.
- Keep live updates and fallback polling behavior stable.
- Avoid adding new architectural layers unless they solve a concrete problem.
- Keep WordPress support auth as a two-step flow: login first, then support API key generation.
- Keep support API and WebSocket behavior aligned with the plugin contract.

## Near-term checks

- Build Android after startup changes.
- Run in emulator.
- Verify splash, shell, and login render.
- Verify live connection and fallback behavior.
