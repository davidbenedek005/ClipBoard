package com.david.clipboardsync

import android.content.ClipboardManager
import android.content.Context
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
            ClipboardRepository.publish(OutgoingClip.Image(image))
            Toast.makeText(context, "Image copied to ClipBoard", Toast.LENGTH_SHORT).show()
            return true
        }
        val text = (0 until clip.itemCount).firstNotNullOfOrNull { index ->
            clip.getItemAt(index).text?.toString()?.takeIf { it.isNotEmpty() }
        } ?: return false
        ClipboardRepository.publish(OutgoingClip.Text(text))
        Toast.makeText(context, "Message copied to ClipBoard", Toast.LENGTH_SHORT).show()
        return true
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
            return ImageCodec.toJpeg(raw)
        }
        return null
    }
}
