# Setup

Phase 1 is the Windows tray listener. Phase 2 is the Android app, opened from Android Studio.

## What you need right now

- Windows 11
- .NET SDK with the Windows Desktop runtime. This machine has the **.NET 10** SDK (`10.0.202`) and `Microsoft.WindowsDesktop.App` 10.0, so `windows-app` targets `net10.0-windows`. The spec asked for .NET 8. The WPF and Win32 calls in Phase 1 are the same on both; retarget by changing `TargetFramework` to `net8.0-windows` after the .NET 8 desktop SDK is installed.
- Android Studio with SDK 35, for the phone app. The repo was written on a machine without a JDK or Android SDK, so Gradle has not been run here. Android Studio will generate the Gradle wrapper jar on first sync.

## Build and run the Windows listener

From the repository root:

```powershell
dotnet build windows-app\ClipboardSync.sln
dotnet run --project windows-app\ClipboardSync.App
```

The process starts in the tray. Look for the blue clipboard icon in the system tray (the overflow area, if it is not pinned).

| Tray action | What it does |
| --- | --- |
| Left-click | Opens the ClipBoard window (Sync history, Pairing, Settings). Closing the window hides it again |
| Right-click → **Exit** | Removes the tray icon and stops the app |

## Confirm clipboard detection

1. Start the app and leave it in the tray. Do not expect a main window.
2. Copy text in Notepad, a browser, or any other app.
3. Open **Status**. A line should appear within a second with the text length and a short preview.
4. Press Print Screen, or copy an image. Status should show an image line with pixel dimensions.
5. Choose **Exit** when finished. Closing the Status window does not quit the app.

A copy of the same log is appended to:

`%LOCALAPPDATA%\ClipBoard\listener.log`

Previews are truncated, but they can still contain passwords or other secrets. The file never leaves the PC. Delete it if you copied something sensitive while testing.

Override the path for a single run by setting `CLIPBOARD_SYNC_LOG` to a full file path before launching.

## Android app (Phase 2)

Open the `android-app` folder in Android Studio (not the repo root) and run the `app` configuration on the Galaxy A56 or an API 26+ emulator.

- Application id: `com.david.clipboardsync`
- minSdk 26, compileSdk and targetSdk 35

On the phone:

1. Open accessibility settings from the app and turn ClipBoard on. The system screen shows why the service is requested.
2. Accept the battery exemption. On One UI, also set ClipBoard to Unrestricted and remove it from Sleeping apps.
3. Skip notifications. The app never asks for that permission; all feedback is by toast.
4. Leave ClipBoard and copy text in Chrome, Messages, or Notes.
5. Open ClipBoard again. The copy should appear under **Detected copies**. Logcat tag `ClipBoardA11y` should show the same line from when the app was backgrounded.
6. **Simulate PC text** only writes a log line. It does not change the phone clipboard.

## Pair and sync text (Phase 3)

Both devices must be on the same Wi-Fi. The PC listens on port **53211**. Clipboard bytes are AES-256-GCM. The WebSocket itself is cleartext (`ws://`), which Android allows because the app sets `usesCleartextTraffic`.

1. Restart the Windows tray app so it picks up this build: `dotnet run --project windows-app\ClipboardSync.App`
2. Left-click the tray icon and open **Pairing**. If Windows Firewall asks, allow ClipBoard on private networks.
3. On the phone, install the updated app. Scan the QR with the phone camera (**Pairing → Open camera**), or type the IP, port, and token under **Manual connection**.
4. The phone notification should change to **Connected**. The PC status tooltip should say the phone is connected.
5. Copy text on the phone, then open ClipBoard. Paste on the PC. Phone → PC waits for that foreground open.
6. Copy text on the PC. Paste on the phone.
7. Copy the same text back and forth once. It should not bounce forever.

## Images, settings, and mDNS (Phase 4)

Phone → PC text still waits until you open ClipBoard. Android 14 and One UI hold that clipboard read. The Quick Settings tile **Sync now** opens the app so a queued copy can go out.

Images are JPEG, longest edge 1600px, at most 5 MB. The encrypted plaintext is the Base64 of those bytes. Settings on both devices can turn images off.

The PC advertises `_clipboardsync._tcp`. After a DHCP change the phone updates the saved address when the advertised device id matches the one from the pairing link. Scan the QR again once so the phone stores that id.

Restart the tray app after this build. On the phone, install from Android Studio, open **Settings**, and add the **Sync now** tile from the quick settings editor.

A rejected pairing token stops reconnect until you scan again. Wi-Fi drops retry after 1, 2, 4, 8, 16, then 30 seconds.

## Installers (Phase 5)

The self-contained Windows publish, the Inno Setup script, and the signed-APK steps are in [PACKAGING.md](PACKAGING.md).
