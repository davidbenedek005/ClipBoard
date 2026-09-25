# Clipboard sync protocol

**Status: not on the wire yet.** `shared/protocol-schema.json` is an empty stub (`{}`). Phase 3 fills that file in and treats it as the only schema shared by the Windows and Android apps. Until then, neither client sends JSON.

The planned message, version 1, is one JSON object per WebSocket text frame:

```json
{
  "version": 1,
  "type": "text | image",
  "originDeviceId": "uuid-of-sending-device",
  "contentHash": "sha256-of-plaintext-content",
  "timestamp": "ISO-8601 UTC",
  "payload": "base64-encoded, AES-GCM-encrypted content",
  "encryption": {
    "iv": "base64",
    "authTag": "base64"
  }
}
```

Rules that Phase 3 must implement:

- `type: "text"` decrypts to UTF-8 text.
- `type: "image"` decrypts to PNG or JPEG bytes (themselves stored as the decrypted payload; the spec's Base64-of-image step is applied before encryption, and the receiver base64-decodes after AES-GCM).
- The receiver checks the pairing-token-derived key, drops the message when `contentHash` equals the hash it last sent (echo suppression), decrypts, then writes the local clipboard.
- Every payload is AES-256-GCM. The key is derived from the pairing token (HKDF or PBKDF2). The token is delivered once inside the pairing QR code, not on every message.
