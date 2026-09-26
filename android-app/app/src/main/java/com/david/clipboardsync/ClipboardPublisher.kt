package com.david.clipboardsync

import android.content.ClipboardManager
import android.content.Context
import android.net.Uri
import android.widget.Toast

/**
 * Reads the clipboard on the calling thread and queues text or a compressed
 * JPEG. The WebSocket sender Base64-encodes the JPEG before encrypting it.
 */
object ClipboardPublisher {
    fun publish(context: Context): Boolean {
        val clip = context.getSystemService(ClipboardManager::class.java).primaryClip ?: return false
        val image = imageJpeg(context, clip)
        if (image != null) {
            if (image.isNotEmpty()) {
                publishJpeg(context, image)
            }
            return true
        }
        val text = (0 until clip.itemCount).firstNotNullOfOrNull { index ->
            clip.getItemAt(index).text?.toString()?.takeIf { it.isNotEmpty() }
        } ?: return false
        publishText(context, text)
        return true
    }

    fun publishText(context: Context, text: String) {
        if (EchoGuard.consumeText(text)) {
            return
        }
        ClipboardRepository.publish(OutgoingClip.Text(text))
        Toast.makeText(context, "Message copied to ClipBoard", Toast.LENGTH_SHORT).show()
    }

    /**
     * Reads [uri] on the calling thread. Share and text-selection URI grants end when the
     * receiving activity finishes, so callers read before calling finish().
     */
    fun publishImage(context: Context, uri: Uri): Boolean {
        val raw = try {
            context.contentResolver.openInputStream(uri)?.use { it.readBytes() }
        } catch (_: Exception) {
            null
        } ?: return false
        if (EchoGuard.consumeImage(raw)) {
            return true
        }
        val jpeg = ImageCodec.toJpeg(raw) ?: return false
        publishJpeg(context, jpeg)
        return true
    }

    private fun publishJpeg(context: Context, jpeg: ByteArray) {
        ClipboardRepository.publish(OutgoingClip.Image(jpeg))
        Toast.makeText(context, "Image copied to ClipBoard", Toast.LENGTH_SHORT).show()
    }

    private fun imageJpeg(context: Context, clip: android.content.ClipData): ByteArray? {
        val description = clip.description
        for (index in 0 until description.mimeTypeCount) {
            if (!description.getMimeType(index).startsWith("image/")) {
                continue
            }
            val uri = if (index < clip.itemCount) clip.getItemAt(index).uri else null
                ?: (0 until clip.itemCount).firstNotNullOfOrNull { clip.getItemAt(it).uri }
                ?: continue
            val raw = context.contentResolver.openInputStream(uri)?.use { it.readBytes() } ?: continue
            if (EchoGuard.consumeImage(raw)) {
                return ByteArray(0)
            }
            return ImageCodec.toJpeg(raw)
        }
        return null
    }
}
