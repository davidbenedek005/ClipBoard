# Architecture

ClipBoard is a two-device LAN sync tool: a Windows 11 tray app hosts a WebSocket server, and an Android app on a Samsung Galaxy A56 is the client. Copy on one device, paste on the other, without a manual send step in the common case.

Personal clipboard data (passwords, OTPs, message text) stays off third-party servers. A cloud relay is optional future work and is not part of the MVP.

## Implementation status

| Phase | Scope | State |
| --- | --- | --- |
| 0 | Repo layout, docs, protocol stub | Done |
| 1 | Windows tray app, event-driven clipboard listener, log only | Done in this tree. Confirm on your PC before Phase 2 |
| 2 | Android accessibility-service clipboard read, foreground service | Code is in `android-app/`. Confirm on a device before Phase 3 |
| 3 | WebSocket, QR pairing, AES-GCM, echo suppression | Code is in both apps. Confirm text sync on the LAN before Phase 4 |
| 4 | Images end-to-end, settings, mDNS, Quick Settings tile | Code is in both apps. Confirm on the LAN before Phase 5 |
| 5 | Installer, signed APK | Scripts and the walkthrough are in [PACKAGING.md](PACKAGING.md) |

The two clients will share nothing except `shared/protocol-schema.json`.

## Windows listener (Phase 1)

The app is a WPF `WinExe` with `ShutdownMode.OnExplicitShutdown`. No main window is shown. `Hardcodet.NotifyIcon.Wpf` 2.0.1 owns the tray icon.

Clipboard changes are not polled. A message-only `HwndSource` (parent `HWND_MESSAGE`) calls `AddClipboardFormatListener`. Windows then posts `WM_CLIPBOARDUPDATE` (`0x031D`) to that HWND. The hook runs on the WPF STA thread, which is required for `System.Windows.Clipboard`. Details and failure modes are commented in `ClipboardMonitor.cs`.

`ClipboardWebSocketServer` is a stub: it records what it would have broadcast. It does not open a port.

Target framework is `net10.0-windows` because this PC has the .NET 10 SDK and Windows Desktop 10 runtime only. The spec's .NET 8 target is a one-line retarget once that SDK is installed.

## Android 10+ clipboard restriction (Phase 2)

Since Android 10, `ClipboardManager.getPrimaryClip()` returns null unless the caller is the default IME, the focused window, or the app that set the clip. A foreground service alone is not exempt. Polling from a background service does not work.

The workaround, which the onboarding screen explains:

1. **AccessibilityService** — the actual exemption. `ClipboardAccessibilityService` listens for `TYPE_WINDOW_STATE_CHANGED` and `TYPE_VIEW_TEXT_SELECTION_CHANGED`, then reads `primaryClip`. The service config must set `canRetrieveWindowContent="true"` and a description that says why. The user enables it by hand via `Settings.ACTION_ACCESSIBILITY_SETTINGS`. It cannot be turned on from code.
2. **Foreground service** — a low-priority ongoing notification keeps the process out of the easiest Doze kills and shows "Connected" / "Disconnected". Onboarding also sends the user to `Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`. Samsung One UI freezes background apps more aggressively than stock Android, so that exemption matters on the Galaxy A56.
3. **Writing** the clipboard (`setPrimaryClip`) is unrestricted. PC → phone can be proven before the accessibility path.

Fully automatic sync is the expected case, not a guarantee under every OEM battery manager. Phase 4 adds a Quick Settings tile ("Sync Clipboard Now") and an optional brief "Sent" overlay.

## Play Store policy

Using `AccessibilityService` only to read the clipboard violates the Play Accessibility API policy when accessibility is not the app's real purpose. Those apps are often rejected or removed.

That is acceptable here: the deliverable is a personal sideloaded APK. Play distribution would need either a genuine accessibility justification (frequently still rejected for clipboard sync) or a rewrite as a custom IME, because the default keyboard is also exempt from the clipboard restriction. The IME design is future work. Do not ship this AccessibilityService build to the Play Store as-is.

## Planned network

- Windows listens on a fixed local port (planned: `53211`).
- First pairing: the PC shows a QR code of `clipboardsync://pair?ip&port&token&device`. The phone camera opens ClipBoard through that link. The same values can be typed on the Manual connection screen. The token is stored in `EncryptedSharedPreferences`.
- After DHCP changes the PC's address, Android NSD (`NsdManager`) resolves `_clipboardsync._tcp` advertised by a Windows mDNS responder. The user should not have to scan again.
- Payloads are AES-256-GCM. Each message has `originDeviceId` and `contentHash`. A device ignores an incoming hash equal to the hash it just sent, which stops the copy → remote set → local change event → send back loop.

## Repo layout

```
ClipBoard/
├── README.md
├── docs/                  architecture, protocol, setup
├── shared/protocol-schema.json
├── windows-app/           WPF tray app (Phase 1 code)
└── android-app/           folder skeleton only until Phase 2
```

The Android application id is `com.david.clipboardsync`. compileSdk and targetSdk are 35. The newest Compose BOM that still builds against API 35 is `2025.08.00` (Compose 1.9) with Android Gradle Plugin 8.7.3. Compose 1.12 requires compileSdk 37, so it is not used.

Phase 2 reads the clipboard from `ClipboardAccessibilityService` and logs it (Logcat tag `ClipBoardA11y`, plus the onboarding screen). It does not open a socket. `ClipboardReceiverService` only logs what a future PC message would write.
