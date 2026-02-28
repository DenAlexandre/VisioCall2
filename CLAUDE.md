# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build & run the SignalR server (listens on http://0.0.0.0:5000)
dotnet run --project Visio/VisioCall.Server

# Build MAUI mobile client (Android)
dotnet build Visio/VisioCall.Maui -f net10.0-android

# Build MAUI mobile client (iOS)
dotnet build Visio/VisioCall.Maui -f net10.0-ios

# Publish Android APK
dotnet publish Visio/VisioCall.Maui -f net10.0-android -c Release -p:EmbedAssembliesIntoApk=true

# Deploy APK to connected Android device via ADB
powershell ./deploy/deploy.ps1 -Release

# Build the Visio task management app
dotnet build Visio/Visio -f net10.0-android
```

No test projects exist in the solution.

## Architecture

This is a cross-platform video calling app with two MAUI client apps and a shared SignalR backend.

### Projects (Visio/Visio.slnx)

- **VisioCall.Server** (net9.0) — ASP.NET Core SignalR backend. `CallHub` handles call signaling and user tracking. `UserTrackingService` maps user IDs to SignalR connection IDs in memory.
- **VisioCall.Maui** (net10.0-android/ios) — Mobile video calling client. Uses SignalR for signaling and WebRTC (via WebView + JavaScript) for peer-to-peer media.
- **VisioCall.Shared** (net9.0/net10.0) — Shared models (`CallRequest`, `CallResponse`, `UserInfo`, `SessionDescription`, `IceCandidate`, `CallState`) and `HubRoutes` constants for SignalR method names.
- **Visio** (net10.0-android/ios/maccatalyst/windows) — Separate task/project management MAUI app with SQLite local storage.

### Video Call Flow

1. Client connects to SignalR hub and registers (userId + displayName)
2. Caller initiates call → server forwards `IncomingCall` to callee
3. Callee accepts → both navigate to `VideoCallPage`
4. WebRTC negotiation happens through a WebView bridge:
   - C# calls JavaScript via `EvaluateJavaScriptAsync` (createOffer, receiveAnswer, etc.)
   - JavaScript sends data back to C# via URL interception (`visiocall://` scheme with JSON payload)
   - SDP offers/answers and ICE candidates are relayed through SignalR

### Key Patterns

- **MVVM**: Pages in `Pages/`, ViewModels in `PageModels/`. Uses CommunityToolkit.MVVM (`[ObservableProperty]`, `[RelayCommand]`).
- **Services**: Singleton services registered in `MauiProgram.cs` — `SignalingService` (SignalR wrapper), `CallService` (call lifecycle), `WebRtcService` (WebView↔JS bridge), `AudioService` (ringtones), `PermissionService`.
- **WebRTC bridge**: `Visio/VisioCall.Maui/Resources/Raw/webrtc/` contains `index.html` and `webrtc.js`. Platform-specific WebView permission handlers exist under `Platforms/Android/Handlers/` and `Platforms/iOS/Handlers/`.
- **SignalR method names**: All hub method names are centralized in `VisioCall.Shared/Constants/HubRoutes.cs` — always use these constants, never hardcode method name strings.

### Development Setup

- The server runs on port 5000 (configured in `appsettings.json` Kestrel section)
- For remote testing, ngrok (`ngrok/ngrok.exe`) exposes the local server; update the server URL in `LoginPageModel.cs`
- CORS is wide open in dev (AllowAnyOrigin)
- WebRTC uses Google's public STUN servers for NAT traversal
