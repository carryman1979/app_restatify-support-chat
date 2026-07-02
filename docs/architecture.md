# Architecture

## Overview

This is an Uno Platform single-project app with a shared UI shell and platform-specific startup hooks.

## Current structure

- Shared app startup lives in `src/SupportChat.App/Restatify.SupportChat.App/App.xaml.cs`.
- Shell navigation starts at `Presentation/Shell.xaml` and then routes into login or main content.
- Main page and conversation pages are the primary runtime surfaces.

## Auth and API shape

- Login is performed against the WordPress support API using username and password.
- The login response is not the final app credential; the app exchanges the bearer token for a support API key.
- Stored settings keep `BaseUrl`, `ApiKey`, `LogoUrl`, language, and theme mode.
- API calls use the support API key in `X-API-Key`.
- WebSocket live updates use the same key in the query string.
- On local development hosts (`localhost` / `127.0.0.1`), the client can fall back to a fixed dev API key when none is stored.

## Runtime path

1. App starts.
2. Shell is created.
3. Authentication state decides whether login or main view is shown.
4. Main view initializes live updates and fallback polling.
5. Conversation view manages message refresh and live reconnect behavior.

## WordPress plugin boundary

- The app is a support client for the WordPress plugin, not a generic chat frontend.
- The API surface includes login, API-key generation, conversation list retrieval, live event subscription, message history, reply posting, AI mode changes, deletion, and booking overlay actions.
- The client normalizes the configured base URL before every request so path differences do not leak into the call sites.

## Constraints

- Avoid reintroducing duplicate startup logic in code-behind.
- Keep generated Uno artifacts untouched.
- Treat platform-specific files as entry points, not places for business logic.
