# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build & run the SignalR server (listens on http://0.0.0.0:5000)
dotnet run --project Visio/VisioCall.Server

# Build MAUI client (Android)
dotnet build Visio/VisioCall.Maui -f net10.0-android

# Build and install APK on connected Android device
dotnet build Visio/VisioCall.Maui -f net10.0-android -t:Install

# Build MAUI client (Windows)
dotnet build Visio/VisioCall.Maui -f net10.0-windows10.0.19041.0

# Build MAUI client (iOS)
dotnet build Visio/VisioCall.Maui -f net10.0-ios

# Build the Visio task management app
dotnet build Visio/Visio -f net10.0-android
```

No test projects exist in the solution.

## Architecture

This is a cross-platform video calling app with two MAUI client apps and a shared SignalR backend.

### Projects (Visio/Visio.slnx)

- **VisioCall.Server** (net9.0) — ASP.NET Core SignalR backend. `CallHub` handles call signaling and user tracking. `UserTrackingService` maps user IDs to SignalR connection IDs in memory.
- **VisioCall.Maui** (net10.0-android/ios/windows) — Cross-platform video calling client. Uses SignalR for signaling and WebRTC (via WebView + JavaScript) for peer-to-peer media.
- **VisioCall.Shared** (net9.0/net10.0) — Shared models (`CallRequest`, `CallResponse`, `UserInfo`, `SessionDescription`, `IceCandidate`, `CallState`) and `HubRoutes` constants for SignalR method names.
- **Visio** (net10.0-android/ios/maccatalyst/windows) — Separate task/project management MAUI app with SQLite local storage.

### Video Call Flow

1. Client connects to SignalR hub and registers (userId + displayName)
2. Caller initiates call → server forwards `IncomingCall` to callee
3. Callee accepts → both navigate to `VideoCallPage`
4. WebRTC negotiation happens through a WebView bridge:
   - C# calls JavaScript via `EvaluateJavaScriptAsync` (createOffer, receiveAnswer, etc.)
   - **Android/iOS**: JS sends data to C# via iframe URL interception (`visiocall://` scheme)
   - **Windows**: JS queues messages in `_outbox[]`, C# polls via `flushMessages()` every 100ms (iframe/postMessage/location.href bridges don't work reliably on WebView2)
   - Uses **vanilla ICE** (all candidates gathered before sending SDP) to avoid trickle ICE message loss
   - SDP offers/answers are relayed through SignalR

### Key Patterns

- **MVVM**: Pages in `Pages/`, ViewModels in `PageModels/`. Uses CommunityToolkit.MVVM (`[ObservableProperty]`, `[RelayCommand]`).
- **Services**: Singleton services registered in `MauiProgram.cs` — `SignalingService` (SignalR wrapper), `CallService` (call lifecycle), `WebRtcService` (WebView↔JS bridge), `AudioService` (ringtones), `PermissionService`.
- **WebRTC bridge**: `Visio/VisioCall.Maui/Resources/Raw/webrtc/` contains `index.html`, `webrtc.js`, `webrtc.css`. Platform-specific handlers under `Platforms/{Android,iOS,Windows}/Handlers/`.
- **SignalR method names**: All hub method names are centralized in `VisioCall.Shared/Constants/HubRoutes.cs` — always use these constants, never hardcode method name strings.

### Development Setup

- The server runs on port 5000 (configured in `appsettings.json` Kestrel section)
- For remote testing, ngrok (`ngrok/ngrok.exe`) exposes the local server; update the server URL in `LoginPageModel.cs`
- CORS is wide open in dev (AllowAnyOrigin)
- WebRTC uses Google's public STUN servers for NAT traversal

### Windows-Specific Notes

- WebView2 requires HTTPS for `getUserMedia()` — HTML is served via virtual host mapping (`https://webrtc.local/`) configured in `WebViewPermissionHandler.cs`
- mDNS obfuscation must be disabled via `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` env var (set in `MauiProgram.cs`). **Kill all `msedgewebview2.exe` processes** before launching if this flag was changed — WebView2 shares browser processes.
- Windows Firewall must allow inbound UDP for `msedgewebview2.exe` (not the app exe — WebView2 opens its own sockets). Network profile may be Public, so rules must cover all profiles.
- `PermissionService` bypasses MAUI camera/mic permissions on Windows (WebView2 handles its own via `PermissionRequested` event).
