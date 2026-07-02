# Authentication

## Overview

The app uses a two-step authentication flow for the WordPress support backend.

1. Sign in with username and password.
2. Exchange the bearer token for a support API key.
3. Store the API key locally and use it for support API calls and live updates.

## Details

- Base URL comes from connection settings.
- The login request is sent to `/v1/auth/login`.
- The API key generation request is sent to `/v1/auth/generate-api-key`.
- After sign-in, the app treats the generated API key as the durable support-session credential.
- The login page can remember username and password locally if the user opts in.

## Local development

- For `localhost` or `127.0.0.1`, the client can use the local dev API key fallback when no API key is stored.
- This keeps emulator and desktop testing usable when the backend is running locally.
- That fallback is for local development only and should not be treated as a production auth mechanism.