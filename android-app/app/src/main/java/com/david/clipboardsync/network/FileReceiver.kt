package com.david.clipboardsync.network

import android.content.ContentValues
import android.content.Context
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.os.Handler
import android.os.Looper
import android.provider.MediaStore
import android.webkit.MimeTypeMap
import android.widget.Toast
import androidx.core.content.FileProvider
import com.david.clipboardsync.ClipboardDebugLog
import com.david.clipboardsync.FileTransfer
import com.david.clipboardsync.FileTransfers
import com.david.clipboardsync.SyncHistory
import com.david.clipboardsync.crypto.EncryptionUtil
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.io.OutputStream

/**
 * PC → phone files. Frames arrive on the OkHttp reader thread one at a time, so
 * each chunk is written before the next is read and the PC is throttled by TCP.
 * Methods are synchronized because stop and reconnect abort from other threads. Android 10+ writes to the public Downloads
 * collection through MediaStore (no storage permission); older releases use the
 * app's own Downloads folder.
 */
internal class FileReceiver(
    private val context: Context,
    private val holdTransferLocks: (Boolean) -> Unit,
    private val rejectToPeer: (transferId: String) -> Unit,
) {
    private class Incoming(
        val id: String,
        val name: String,
        val mimeType: String,
        val size: Long,
        val chunkSize: Int,
        val key: ByteArray,
        val target: Target,
        val output: OutputStream,
    ) {
        var nextIndex = 0
        var written = 0L
    }

    private class Target(val uri: Uri, val legacyFile: File?)

    private val active = HashMap<String, Incoming>()

    @Synchronized
    fun begin(meta: JSONObject, key: ByteArray) {
        val id = meta.getString("transferId")
        val size = meta.getLong("fileSize")
        val chunkSize = meta.optInt("chunkSize", FileProtocol.CHUNK_SIZE)
        if (id.length != FileProtocol.ID_SIZE * 2 || size < 0 || chunkSize <= 0 || chunkSize > 4 * FileProtocol.CHUNK_SIZE) {
            ClipboardDebugLog.record("file", "dropped offer with bad metadata")
            return
        }
        val name = safeName(meta.optString("fileName"))
        val mimeType = mimeFor(name, meta.optString("mimeType"))
        active.remove(id)?.let { discard(it) }
        val target = try {
            create(name, mimeType)
        } catch (ex: Exception) {
            ClipboardDebugLog.record("file", "cannot create $name: ${ex.message}")
            return
        }
        val output = try {
            open(target)
        } catch (ex: Exception) {
            delete(target)
            ClipboardDebugLog.record("file", "cannot open $name: ${ex.message}")
            return
        }
        active[id] = Incoming(id, name, mimeType, size, chunkSize, key, target, output)
        holdTransferLocks(true)
        FileTransfers.begin(FileTransfer(id, name, sending = false, totalBytes = size))
        ClipboardDebugLog.record("file", "receiving $name bytes=$size")
    }

    @Synchronized
    fun chunk(frame: ByteArray) {
        if (frame.size < FileProtocol.OVERHEAD) {
            return
        }
        val id = FileProtocol.transferIdHex(frame)
        val incoming = active[id] ?: return
        val length = frame.size - FileProtocol.OVERHEAD
        if (FileProtocol.index(frame) != incoming.nextIndex ||
            length > incoming.chunkSize ||
            incoming.written + length > incoming.size
        ) {
            abort(id, "chunk out of order or oversized")
            return
        }
        try {
            val plain = EncryptionUtil.openChunk(incoming.key, frame, FileProtocol.HEADER_SIZE)
            incoming.output.write(plain)
        } catch (ex: Exception) {
            abort(id, ex.message ?: "write failed")
            return
        }
        incoming.nextIndex += 1
        incoming.written += length
        FileTransfers.progress(id, incoming.written, incoming.size)
    }

    @Synchronized
    fun end(meta: JSONObject) {
        val id = meta.getString("transferId")
        val incoming = active.remove(id) ?: return
        val complete = meta.optInt("chunks", -1) == incoming.nextIndex &&
            meta.optLong("fileSize", -1) == incoming.size &&
            incoming.written == incoming.size
        if (!complete) {
            SyncHistory.addText("Skipped", "${incoming.name} arrived incomplete")
            discard(incoming)
            return
        }
        try {
            incoming.output.close()
            publish(incoming.target)
            SyncHistory.addFile("Received", incoming.name, incoming.size, incoming.mimeType, incoming.target.uri.toString())
            ClipboardDebugLog.record("file", "saved ${incoming.name}")
            toast("${incoming.name} saved to Downloads")
        } catch (ex: Exception) {
            ClipboardDebugLog.record("file", "could not finish ${incoming.name}: ${ex.message}")
            delete(incoming.target)
        } finally {
            finish(incoming)
        }
    }

    @Synchronized
    fun cancel(meta: JSONObject) {
        abort(meta.getString("transferId"), "the PC cancelled it", notifyPeer = false)
    }

    @Synchronized
    fun abortAll(reason: String) {
        active.keys.toList().forEach { abort(it, reason) }
    }

    private fun abort(id: String, reason: String, notifyPeer: Boolean = true) {
        val incoming = active.remove(id) ?: return
        ClipboardDebugLog.record("file", "discarded ${incoming.name}: $reason")
        if (notifyPeer) {
            rejectToPeer(id)
        }
        SyncHistory.addText("Skipped", "${incoming.name} was not received")
        discard(incoming)
    }

    private fun discard(incoming: Incoming) {
        runCatching { incoming.output.close() }
        delete(incoming.target)
        finish(incoming)
    }

    private fun finish(incoming: Incoming) {
        FileTransfers.end(incoming.id)
        holdTransferLocks(false)
    }

    private fun create(name: String, mimeType: String): Target {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            val values = ContentValues().apply {
                put(MediaStore.Downloads.DISPLAY_NAME, name)
                put(MediaStore.Downloads.MIME_TYPE, mimeType)
                put(MediaStore.Downloads.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS)
                put(MediaStore.Downloads.IS_PENDING, 1)
            }
            val collection = MediaStore.Downloads.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY)
            val uri = context.contentResolver.insert(collection, values)
                ?: throw IOException("MediaStore refused the download")
            return Target(uri, null)
        }
        val folder = context.getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS)
            ?: File(context.filesDir, "downloads")
        folder.mkdirs()
        val file = uniqueFile(folder, name)
        file.createNewFile()
        val uri = FileProvider.getUriForFile(context, "${context.packageName}.files", file)
        return Target(uri, file)
    }

    private fun open(target: Target): OutputStream =
        target.legacyFile?.let { FileOutputStream(it) }
            ?: context.contentResolver.openOutputStream(target.uri, "w")
            ?: throw IOException("no output stream")

    private fun publish(target: Target) {
        if (target.legacyFile == null && Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            val values = ContentValues().apply { put(MediaStore.Downloads.IS_PENDING, 0) }
            context.contentResolver.update(target.uri, values, null, null)
        }
    }

    private fun delete(target: Target) {
        runCatching {
            if (target.legacyFile != null) {
                target.legacyFile.delete()
            } else {
                context.contentResolver.delete(target.uri, null, null)
            }
        }
    }

    private fun toast(text: String) {
        Handler(Looper.getMainLooper()).post {
            Toast.makeText(context, text, Toast.LENGTH_SHORT).show()
        }
    }

    private fun uniqueFile(folder: File, name: String): File {
        val stem = name.substringBeforeLast('.', name)
        val extension = name.substringAfterLast('.', "").let { if (it.isEmpty()) "" else ".$it" }
        var candidate = File(folder, name)
        var n = 1
        while (candidate.exists()) {
            candidate = File(folder, "$stem ($n)$extension")
            n += 1
        }
        return candidate
    }

    private fun safeName(raw: String): String {
        val name = raw.replace('\\', '/').substringAfterLast('/')
            .replace(Regex("[\\u0000-\\u001f\"*:<>?|]"), "_")
            .trim()
            .trimStart('.')
        return name.take(180).ifEmpty { "file" }
    }

    /** MediaStore renames files whose extension disagrees with MIME_TYPE, so prefer the type implied by the name. */
    private fun mimeFor(name: String, declared: String): String {
        val extension = name.substringAfterLast('.', "").lowercase()
        return MimeTypeMap.getSingleton().getMimeTypeFromExtension(extension)
            ?: declared.takeIf { it.contains('/') }
            ?: "application/octet-stream"
    }
}
