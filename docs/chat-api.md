# Chat API

## Overview

This app is a support client for the WordPress plugin backend.
The client talks to the support API for list views, message actions, live updates, and conversation tools.

## Endpoints

- `GET /v1/support/conversations`
- `GET /v1/support/conversations/{conversation_id}/messages`
- `POST /v1/support/conversations/{conversation_id}/reply`
- `GET /v1/support/conversations/{conversation_id}/tools`
- `PUT /v1/support/conversations/{conversation_id}/ai-mode`
- `DELETE /v1/support/conversations/{conversation_id}`
- `POST /v1/support/conversations/{conversation_id}/open-booking-overlay`
- `GET /v1/support/ws/updates`

## Request rules

- Support API requests use `X-API-Key`.
- Live WebSocket updates use `api_key` in the query string.
- The base URL is normalized before requests so only the configured host and path root matter.
- HTTP connection issues are retried once before the client surfaces a failure.

## Live events

- Live messages are parsed from JSON payloads with a `type` field.
- The client currently expects events such as `connected`, `message_added`, and `conversation_deleted`.
- When live updates fail, the UI falls back to polling rather than blocking the operator.

## Operator behavior

- Conversation history should load predictably even after reconnects.
- Reply, tools, AI mode, and deletion actions should be resilient to transient connection loss.
- If the plugin or local backend is unavailable, show a direct operator-facing error rather than failing silently.