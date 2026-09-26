package com.david.clipboardsync.service

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.util.Log
import androidx.core.content.FileProvider
import com.david.clipboardsync.ClipboardDebugLog
import com.david.clipboardsync.EchoGuard
import com.david.clipboardsync.crypto.EncryptionUtil
import java.io.File

/**
 * PC → phone writes. [ClipboardManager.setPrimaryClip] works from the background.
 * [EchoGuard.expectText] and [EchoGuard.expectImage] run first so a later send
 * of this same clip is treated as an echo.
 */
object ClipboardReceiverService {
    private const val TAG = "ClipBoardReceive"

    fun applyText(context: Context, text: String) {
        EchoGuard.expectText(text)
        val clipboard = context.getSystemService(ClipboardManager::class.java)
        clipboard.setPrimaryClip(ClipData.newPlainText("ClipBoard", text))
        Log.i(TAG, "setPrimaryClip text len=${text.length}")
    }

    fun applyImage(context: Context, jpeg: ByteArray) {
        EchoGuard.expectImage(jpeg)
        EchoGuard.noteApplied(EncryptionUtil.sha256Hex(jpeg))
        val dir = File(context.cacheDir, "clipboard")
        dir.mkdirs()
        val file = File(dir, "incoming.jpg")
        file.writeBytes(jpeg)
        val uri = FileProvider.getUriForFile(context, "${context.packageName}.files", file)
        val clip = ClipData.newUri(context.contentResolver, "ClipBoard image", uri)
        context.getSystemService(ClipboardManager::class.java).setPrimaryClip(clip)
        Log.i(TAG, "setPrimaryClip image bytes=${jpeg.size}")
    }

    fun wouldWriteText(text: String) {
        val preview = text.replace(Regex("\\s+"), " ").trim().let { flat ->
            if (flat.length <= 80) flat else flat.take(80) + "…"
        }
        val summary = "would setPrimaryClip text len=${text.length} \"$preview\""
        Log.i(TAG, summary)
        ClipboardDebugLog.record("pc-stub", summary)
    }
}
