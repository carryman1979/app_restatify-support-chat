# Support Chat Cursor Playbook

This runbook defines how cursor pagination errors are handled end-to-end for support chat.

## Scope

- API endpoint: `GET /v1/support/conversations/{conversation_id}/messages`
- Cursor format: signed payload with `t`, `m`, `iat`
- Goal: stable paging under retries, stale UI sessions, and tampered links

## API Error Contract

The API returns structured errors in `detail`:

```json
{
  "detail": {
    "code": "cursor_expired",
    "message": "Cursor expired."
  }
}
```

Supported cursor-related codes:

| HTTP | code | Meaning | Client action |
|---|---|---|---|
| 400 | invalid_cursor | Cursor malformed, tampered, or payload invalid | Stop load-more flow, clear cursor, refresh first page |
| 410 | cursor_expired | Cursor is valid but older than TTL | Clear cursor and refresh first page automatically |

## Client Handling Rules

1. For `cursor_expired`:
- Show short status text (for example: "Cursor expired. Reloading latest messages...").
- Reset paging state (`nextCursor = null`).
- Trigger first-page reload.
- Do not show a full-screen error.

2. For `invalid_cursor`:
- Treat as non-recoverable for current paging chain.
- Clear paging state.
- Refresh first page once.
- If refresh fails, show the HTTP error details.

3. Retry policy:
- Automatic retry only for first-page reload after `cursor_expired`.
- Do not loop retries for repeated failures.

## TTL Baseline by Environment

Use short TTLs in production and longer TTLs in dev for debugging:

| Environment | Recommended `cursor_ttl_seconds` |
|---|---|
| dev | 900 (15 min) |
| stage | 600 (10 min) |
| prod | 300 (5 min) |

## Backend Configuration

- Setting key: `cursor_ttl_seconds`
- Suggested env var: `CURSOR_TTL_SECONDS`
- Rotate `cursor_signing_key` if secret leakage is suspected.

## Verification Checklist

1. API unit tests pass:
- tampered cursor -> `400` + `invalid_cursor`
- expired cursor -> `410` + `cursor_expired`

2. App behavior:
- Load more with fresh cursor appends messages.
- Load more with expired cursor auto-refreshes first page.
- Status text is understandable for support agents.

## Operational Notes

- Cursor expiry is a paging-state lifetime, not message retention.
- If UX reports frequent cursor expiry during active work, increase TTL in small increments (for example +120s) and observe.
