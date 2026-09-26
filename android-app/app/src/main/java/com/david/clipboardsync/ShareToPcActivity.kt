package com.david.clipboardsync

import android.app.Activity
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.widget.Toast
import androidx.core.content.IntentCompat
import com.david.clipboardsync.network.SyncController
import com.david.clipboardsync.service.ClipboardForegroundService

/**
 * Invisible entry point for "Send to PC" in the text selection menu
 * (ACTION_PROCESS_TEXT) and for ClipBoard in the share sheet (ACTION_SEND and
 * ACTION_SEND_MULTIPLE). Shared text and images go to the PC clipboard; any
 * other file is streamed to the PC's Downloads folder. It queues the content,
 * toasts, and finishes without showing any UI.
 *
 * No result is set for ACTION_PROCESS_TEXT, so the source app leaves the
 * selected text unchanged.
 */
class ShareToPcActivity : Activity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (savedInstanceState == null) {
            handle(intent)
        }
        finish()
    }

    private fun handle(intent: Intent) {
        if (SyncController.loadPairing(this) == null) {
            Toast.makeText(this, "Open ClipBoard and pair with your PC first", Toast.LENGTH_LONG).show()
            return
        }
        ClipboardForegroundService.start(this)

        when (intent.action) {
            Intent.ACTION_PROCESS_TEXT -> sendText(intent.getCharSequenceExtra(Intent.EXTRA_PROCESS_TEXT))
            Intent.ACTION_SEND -> {
                val stream = sharedStream(intent)
                when {
                    stream == null -> sendText(intent.getCharSequenceExtra(Intent.EXTRA_TEXT))
                    intent.type?.startsWith("image/") == true -> sendImage(stream)
                    else -> FilePublisher.publish(this, stream)
                }
            }
            Intent.ACTION_SEND_MULTIPLE -> {
                val streams = IntentCompat.getParcelableArrayListExtra(intent, Intent.EXTRA_STREAM, Uri::class.java)
                    ?: intent.clipData?.let { clip -> (0 until clip.itemCount).mapNotNull { clip.getItemAt(it).uri } }
                    ?: emptyList()
                if (streams.isEmpty()) {
                    Toast.makeText(this, "Nothing to send", Toast.LENGTH_SHORT).show()
                }
                // Descriptors are opened here, while the share grant is still valid.
                streams.forEach { FilePublisher.publish(this, it) }
            }
        }
    }

    private fun sendText(text: CharSequence?) {
        val value = text?.toString()?.takeIf { it.isNotBlank() }
        if (value == null) {
            Toast.makeText(this, "Nothing to send", Toast.LENGTH_SHORT).show()
            return
        }
        ClipboardPublisher.publishText(this, value)
    }

    private fun sendImage(uri: Uri?) {
        if (!AppSettings(this).syncImages) {
            Toast.makeText(this, "Image sync is off in ClipBoard settings", Toast.LENGTH_SHORT).show()
            return
        }
        if (uri == null || !ClipboardPublisher.publishImage(this, uri)) {
            Toast.makeText(this, "That image could not be sent", Toast.LENGTH_SHORT).show()
        }
    }

    /** Most apps put the file in EXTRA_STREAM; some only attach it as ClipData. */
    private fun sharedStream(intent: Intent): Uri? =
        IntentCompat.getParcelableExtra(intent, Intent.EXTRA_STREAM, Uri::class.java)
            ?: intent.clipData?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.uri
}
