# Clipboard sync protocol

**Status: not on the wire yet.** `shared/protocol-schema.json` is an empty stub (`{}`). Phase 3 fills that file in and treats it as the only schema shared by the Windows and Android apps. Until then, neither client sends JSON.

The planned message, version 1, is one JSON object per WebSocket text frame:

```json
{
  "version": 1,
  "type": "text | image",
  "originDeviceId": "uuid-of-sending-device",
  "deviceName": "David's Phone",
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

## File transfer

Files use the same socket and key, so there is no second port or firewall rule. Neither side ever holds more than one chunk in memory.

1. The sender sends a normal envelope with `type: "file"`. The decrypted payload is JSON:

   ```json
   { "transferId": "32 hex chars", "fileName": "report.pdf", "fileSize": 524288000,
     "mimeType": "application/pdf", "chunkSize": 262144 }
   ```

2. It then sends the file as binary WebSocket frames, chunk 0, 1, 2… in order:

   | Bytes | Content |
   | --- | --- |
   | 0–15 | transfer id |
   | 16–19 | chunk index, uint32 big-endian |
   | 20–31 | AES-GCM nonce, random per chunk |
   | 32 … end−16 | ciphertext (at most `chunkSize` bytes of plaintext) |
   | last 16 | GCM tag |

   Bytes 0–19 are the GCM associated data, so a chunk cannot be moved to another transfer or position.

3. It finishes with `type: "file-end"`, payload `{ "transferId", "chunks", "fileSize" }`. The receiver keeps the file only if the chunk count and byte count match.

Either side may send `type: "file-cancel"`, payload `{ "transferId", "reason" }`. The receiver deletes the partial file, and a sender stops.

Flow control: the receiver writes each chunk before reading the next frame, so TCP throttles the sender. The Android sender also waits while OkHttp's outgoing queue is over 1 MiB, because OkHttp closes the socket past 16 MiB. The Windows sender awaits every Fleck send under one lock.

Saving: Windows writes `<name>.<id>.part` in the user's Downloads folder and renames it on `file-end`. Android 10+ inserts an `IS_PENDING` row into `MediaStore.Downloads` and clears the flag on `file-end`; Android 8–9 writes to the app's own Downloads folder.

Older app versions log and drop the unknown `file*` types and ignore binary frames.

## Several devices

One Windows PC is the hub. Phones and other PCs are spokes. They all share that hub's pairing token, so the hub forwards a ciphertext as-is and does not send it back to the socket it came from.

`deviceName` is the sender's display name. History shows it as "From David's Phone". Older clients omit it.

A second PC joins by opening an invite link from the hub, `clipboardsync://connect?ip=192.168.1.50&port=53211&pin=123456`. The hub creates that 6-digit PIN before anyone connects. The link works once and expires after 10 minutes. The other PC can also type the IP and PIN by hand.

That PC then opens a WebSocket without a token. Until the PIN succeeds, the hub accepts only these messages:

- `{"v":1,"type":"pair-hello","deviceId","deviceName"}`
- `{"v":1,"type":"pair-pin","pin":"048291"}` — five tries, then the socket closes. Twenty failures from one address in ten minutes are refused. The PIN has to match the one in the current invite link.

On success the hub sends `{"v":1,"type":"pair-ok","token","hubDeviceId","hubName","port"}`. Later connections send that token in `X-Clipboard-Token`, plus `X-Clipboard-Device` and `X-Clipboard-Name`. Phones keep using the QR code, which already carries the token.
