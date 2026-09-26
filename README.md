# ClipBoard

Copy on one device. Paste on another. ClipBoard syncs text, images, and files between your Windows PCs and Android phones over your own Wi-Fi.

No account. No cloud. The PC on your desk is the hub, and everything else connects to it.

![Screenshot](link)

## Key features

- **Hub-and-spoke.** One Windows PC is the hub. Phones and other PCs join it and share the same clipboard. A copy from any device is delivered to the others, and it is not sent back to the device that copied it.
- **Pair a phone with the camera.** The hub shows a QR code. Open the phone’s camera, scan it, and the phone connects. You can also type the hub address by hand.
- **Pair another PC with a 6-digit PIN.** The hub shows its LAN address and a one-time PIN. Enter both on the other PC. The PIN expires, and it can only be used once.
- **Large files, streamed.** Send a file of any size. ClipBoard writes it in chunks, so the whole file is never loaded into memory.
- **Windows app.** A Fluent-style WPF window, a tray icon, and an optional start at sign-in.
- **Android app.** Material 3. History, pairing, and settings live in one screen.
- **Send from anywhere on Android.** Quick Settings tile, the share sheet, and **Send to PC** in the text-selection menu. Android does not let ordinary apps read the clipboard in the background, so sending is always something you choose.

![Screenshot](link)

## Privacy and security

ClipBoard stays on your local network.

- Payloads are encrypted with **AES-256-GCM**. The key is derived from the pairing token stored on your devices.
- Traffic is a WebSocket between your machines. There is no ClipBoard server on the internet.
- The Android app does **not** use an Accessibility Service, and it does not ask for broad or unrelated permissions. You send a clip when you tap the tile, share, or **Send to PC**.

The WebSocket itself is cleartext `ws://` on your LAN. The clipboard bytes inside it are encrypted.

## Install

Downloads are on [GitHub Releases](https://github.com/davidbenedek005/ClipBoard/releases).

### Windows

1. Download **ClipBoard-Setup.exe** from the latest release.
2. Run it. The install is per user and does not need an administrator.
3. Leave **Start ClipBoard when I sign in** checked if you want the tray icon after you log in.

Windows 11, 64-bit. The installer includes the .NET runtime, so the other PC does not need a separate .NET install.

### Android

1. Download the **APK** from the same release.
2. Open it on the phone and allow installation from that source when Android asks.
3. Put the phone on the same Wi-Fi as the hub PC.
4. In ClipBoard on the phone, scan the QR code with the phone camera, or enter the hub address yourself.
5. If History shows **Keep ClipBoard connected**, tap **Allow** so Android does not put the connection to sleep.

Android 8 or newer.

![Screenshot](link)

## Use it

1. Start ClipBoard on the Windows PC that should be the hub.
2. Pair a phone from the QR card, or pair another PC with the address and 6-digit PIN on the hub.
3. Copy on Windows. It shows up on the other paired devices.
4. On Android, send with the Quick Settings tile, the share sheet, or **Send to PC**.

The sidebar dot is green when at least one device is connected, or when this PC has joined another hub.

## Tech stack

| | |
| --- | --- |
| Windows | C#, WPF, .NET 10 |
| Android | Kotlin, Jetpack Compose, Material 3 |
| Network | WebSocket on the LAN. The hub listens on port **53211**. |
| Crypto | AES-256-GCM, key from the pairing token |

## Build it yourself

- Windows installer: [docs/PACKAGING.md](docs/PACKAGING.md)
- Run from source: [docs/SETUP.md](docs/SETUP.md)
- Wire format: [docs/protocol.md](docs/protocol.md)
