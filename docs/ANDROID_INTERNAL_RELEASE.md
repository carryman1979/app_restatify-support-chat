# Android Internal Release (Option B)

This runbook prepares a release-signed internal Android package for first live use.

## 1) Prerequisites

- .NET SDK installed for Uno workload
- Android SDK configured on build machine
- Keystore available (release signing)
- Production Support API URL: `https://api.example.test`

## 2) Required signing values

Set environment variables before building:

```powershell
$env:ANDROID_KEYSTORE_PATH = "C:\secrets\restatify-supportchat.keystore"
$env:ANDROID_KEY_ALIAS = "restatify_supportchat"
$env:ANDROID_KEYSTORE_PASSWORD = "<replace>"
$env:ANDROID_KEY_PASSWORD = "<replace>"
```

## 3) Build release APK

Run from repo root:

```powershell
dotnet publish .\src\SupportChat.App\Restatify.SupportChat.App\Restatify.SupportChat.csproj `
  -c Release `
  -f net10.0-android `
  -p:AndroidKeyStore=true `
  -p:AndroidSigningKeyStore=$env:ANDROID_KEYSTORE_PATH `
  -p:AndroidSigningStorePass=$env:ANDROID_KEYSTORE_PASSWORD `
  -p:AndroidSigningKeyAlias=$env:ANDROID_KEY_ALIAS `
  -p:AndroidSigningKeyPass=$env:ANDROID_KEY_PASSWORD `
  -p:AndroidPackageFormat=apk
```

Expected output folder (example):

```text
src\SupportChat.App\Restatify.SupportChat.App\bin\Release\net10.0-android\publish\
```

## 4) Install on internal device

```powershell
adb install -r .\src\SupportChat.App\Restatify.SupportChat.App\bin\Release\net10.0-android\publish\*.apk
```

## 5) First-live smoke test

1. Open app on Android.
2. Set base URL to `https://api.example.test`.
3. Login with WordPress support credentials.
4. Open conversation list.
5. Send one support reply.
6. Confirm live update reception (WebSocket) and fallback behavior.

## 6) If build signing fails

- Verify keystore path and alias.
- Verify passwords exactly match keystore and key.
- Verify device allows installs from trusted internal source.
