# ClipBoard

ClipBoard keeps copied text and images in sync between a Samsung Galaxy A56 and a Windows 11 PC on the same Wi-Fi. The PC hosts a local WebSocket server; the phone connects as a client. Clipboard contents stay on the LAN and are encrypted with a key derived from a one-time pairing token. There is no cloud service in the current design.

This repository is being built in phases. **Phase 4** syncs text and images on the LAN, with settings and mDNS. Phone → PC copies are sent when ClipBoard is brought to the foreground.

- Setup and how to run what exists today: [docs/SETUP.md](docs/SETUP.md)
- Windows installer and signed APK: [docs/PACKAGING.md](docs/PACKAGING.md)
- Architecture, including the Android 10 clipboard restriction: [docs/architecture.md](docs/architecture.md)
- Planned wire format: [docs/protocol.md](docs/protocol.md)
