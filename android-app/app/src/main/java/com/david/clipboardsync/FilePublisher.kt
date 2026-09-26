package com.david.clipboardsync

import android.content.Context
import android.net.Uri
import android.provider.OpenableColumns
import android.widget.Toast
import com.david.clipboardsync.service.ClipboardForegroundService

/** Queues a picked or shared file for the PC. Only metadata and a file descriptor are held, never the bytes. */
object FilePublisher {

    fun publish(context: Context, uri: Uri): Boolean {
        val attachment = open(context, uri)
        if (attachment == null) {
            Toast.makeText(context, "That file could not be read", Toast.LENGTH_SHORT).show()
            return false
        }
        ClipboardForegroundService.start(context)
        ClipboardRepository.publish(attachment)
        Toast.makeText(context, "Sending ${attachment.name} to PC", Toast.LENGTH_SHORT).show()
        return true
    }

    private fun open(context: Context, uri: Uri): OutgoingClip.Attachment? {
        val resolver = context.contentResolver
        var name: String? = null
        var size = -1L
        try {
            resolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE), null, null, null)?.use { cursor ->
                if (cursor.moveToFirst()) {
                    val nameIndex = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                    val sizeIndex = cursor.getColumnIndex(OpenableColumns.SIZE)
                    if (nameIndex >= 0 && !cursor.isNull(nameIndex)) name = cursor.getString(nameIndex)
                    if (sizeIndex >= 0 && !cursor.isNull(sizeIndex)) size = cursor.getLong(sizeIndex)
                }
            }
            val descriptor = resolver.openFileDescriptor(uri, "r") ?: return null
            if (size < 0) {
                size = descriptor.statSize
            }
            if (size < 0) {
                // Pipes and streams without a length cannot be announced up front.
                descriptor.close()
                return null
            }
            return OutgoingClip.Attachment(
                name = name ?: uri.lastPathSegment?.substringAfterLast('/') ?: "file",
                size = size,
                mimeType = resolver.getType(uri) ?: "application/octet-stream",
                descriptor = descriptor,
            )
        } catch (_: Exception) {
            return null
        }
    }
}
