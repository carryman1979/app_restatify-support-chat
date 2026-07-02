# Support Chat Context

Use these files first when working in this repo:

- `README.md` for project scope and build entry points
- `docs/architecture.md` for app structure and startup flow
- `docs/design.md` for UI and styling rules
- `docs/interactions.md` for runtime behavior and UX timing
- `docs/authentication.md` for WordPress login and API-key handling
- `docs/chat-api.md` for the support-chat HTTP and WebSocket contract
- `docs/ux-flows.md` for primary user journeys
- `docs/plan.md` for current work priorities

## Working rules

- Prefer the existing Uno single-project structure over adding new layers prematurely.
- Keep Android, desktop, and web startup paths aligned unless a platform-specific reason exists.
- Do not edit generated files under `obj/` or `bin/`.
- Keep shared logic centralized; avoid duplicating behavior across presentation surfaces.
- Preserve the current login -> shell -> conversation flow unless a change explicitly targets it.
- When changing Android startup behavior, verify both emulator deployment and splash/navigation behavior.
- WordPress support chat auth is two-step: login with username/password, then exchange the bearer token for a support API key.
- Support API calls use `X-API-Key`; live updates use the same key in the WebSocket query string.
- Local dev on `localhost` or `127.0.0.1` can fall back to the workspace dev API key when no key is stored.
