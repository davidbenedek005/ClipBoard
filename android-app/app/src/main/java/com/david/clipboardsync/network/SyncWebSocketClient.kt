package com.david.clipboardsync.network

import android.content.Context
import android.util.Log
import android.util.Base64
import com.david.clipboardsync.AppSettings
import com.david.clipboardsync.ClipboardDebugLog
import com.david.clipboardsync.ClipboardRepository
import com.david.clipboardsync.EchoGuard
import com.david.clipboardsync.ImageCodec
import com.david.clipboardsync.OutgoingClip
import com.david.clipboardsync.SyncHistory
import com.david.clipboardsync.crypto.EncryptionUtil
import com.david.clipboardsync.service.ClipboardForegroundService
import com.david.clipboardsync.service.ClipboardReceiverService
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import okio.ByteString
import okio.ByteString.Companion.toByteString
import org.json.JSONObject
import android.os.ParcelFileDescriptor
import android.os.PowerManager
import android.os.SystemClock
import com.david.clipboardsync.FileTransfer
import com.david.clipboardsync.FileTransfers
import java.io.IOException
import java.io.InputStream
import java.security.SecureRandom
import java.time.Instant
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledFuture
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Phone-side client. The pairing token is the WebSocket upgrade header
 * `X-Clipboard-Token`. Reconnect uses exponential backoff capped at 30 seconds.
 * A policy-violation close (1008) means the token was rejected; backoff stops
 * until the user scans a new QR.
 */
class SyncWebSocketClient(
    context: Context,
    private val pairing: PairingManager,
    private val withNetwork: (() -> Unit) -> Unit,
) {
    private val appContext = context.applicationContext
    private val http = OkHttpClient.Builder()
        .pingInterval(15, TimeUnit.SECONDS)
        .build()
    private val scheduler = Executors.newSingleThreadScheduledExecutor()
    private val stopped = AtomicBoolean(false)
    private val running = AtomicBoolean(false)

    @Volatile
    private var socket: WebSocket? = null

    /** Files are sent one at a time, off the OkHttp and scheduler threads. */
    private val fileExecutor = Executors.newSingleThreadExecutor()

    private val transferWakeLock = appContext.getSystemService(PowerManager::class.java)
        .newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "ClipBoard:file")
        .apply { setReferenceCounted(true) }

    private val receiver = FileReceiver(appContext, ::holdTransferLocks) { id ->
        socket?.let { sendCancel(it, id, "rejected") }
    }
    private var retry: ScheduledFuture<*>? = null
    private var attempt = 0

    private var generation = 0

    @Volatile
    private var pending: OutgoingClip? = null

    private val discovery = DiscoveryManager(appContext, pairing) { _, _ ->
        if (!isManualDisconnect) {
            reconnectNow()
        }
    }

    private val settings = AppSettings(appContext)

    @Volatile
    var isManualDisconnect: Boolean = false
        private set

    @Volatile
    var status: String = "Disconnected"
        private set

    fun start() {
        if (isManualDisconnect) {
            return
        }
        stopped.set(false)
        if (!running.compareAndSet(false, true)) {
            return
        }
        discovery.start()
        connect()
    }

    fun stop() {
        isManualDisconnect = true
        stopped.set(true)
        running.set(false)
        discovery.stop()
        retry?.cancel(false)
        socket?.close(1000, "stop")
        socket = null
        receiver.abortAll("disconnected")
        setStatus("Disconnected")
    }

    fun reconnectNow() {
        isManualDisconnect = false
        stopped.set(false)
        running.set(true)
        attempt = 0
        retry?.cancel(false)
        socket?.cancel()
        socket = null
        receiver.abortAll("reconnecting")
        connect()
    }

    fun send(clip: OutgoingClip): Boolean {
        if (clip is OutgoingClip.Attachment) {
            enqueueFile(clip)
            return true
        }
        pending = clip
        val current = socket
        if (current == null) {
            if (isManualDisconnect) {
                return false
            }
            start()
            return false
        }
        return write(current, clip)
    }

    private fun connect() {
        if (stopped.get() || isManualDisconnect) {
            return
        }
        val record = pairing.load()
        if (record == null) {
            running.set(false)
            setStatus("Disconnected")
            return
        }
        ClipboardDebugLog.record("sync", "connecting to ${record.ip}")
        generation += 1
        val generationAtConnect = generation
        val request = Request.Builder()
            .url("ws://${record.ip}:${record.port}/")
            .header("X-Clipboard-Token", record.token)
            .header("X-Clipboard-Device", pairing.deviceId)
            .header("X-Clipboard-Name", DeviceLabel.header(appContext))
            .build()
        withNetwork {
            socket = http.newWebSocket(request, listener(generationAtConnect))
        }
    }

    private fun scheduleReconnect() {
        if (isManualDisconnect || stopped.get() || pairing.load() == null) {
            return
        }
        if (status != "Disconnected") {
            setStatus("Disconnected")
        }
        val shift = attempt.coerceAtMost(5)
        val delayMs = 1000L shl shift
        attempt += 1
        ClipboardDebugLog.record("sync", "reconnect in ${delayMs / 1000}s")
        retry?.cancel(false)
        retry = scheduler.schedule({ connect() }, delayMs, TimeUnit.MILLISECONDS)
    }

    private fun setStatus(text: String) {
        if (status == text) {
            return
        }
        val justConnected = text == "Successfully connected"
        status = text
        ClipboardRepository.setLinkStatus(text)
        ClipboardDebugLog.record("sync", text)
        ClipboardForegroundService.noteStatus(appContext, text)
        if (justConnected) {
            android.os.Handler(android.os.Looper.getMainLooper()).post {
                android.widget.Toast.makeText(
                    appContext,
                    "Successfully connected",
                    android.widget.Toast.LENGTH_SHORT,
                ).show()
            }
        }
    }

    private fun listener(generationAtConnect: Int) = object : WebSocketListener() {
        private fun current() = generationAtConnect == generation

        override fun onOpen(webSocket: WebSocket, response: Response) {
            if (!current()) {
                webSocket.close(1000, "replaced")
                return
            }
            attempt = 0
            isManualDisconnect = false
            setStatus("Successfully connected")
            Log.i(TAG, "WebSocket open")
            pending?.let { queued -> write(webSocket, queued) }
        }

        override fun onMessage(webSocket: WebSocket, text: String) {
            if (current()) {
                handleMessage(text)
            }
        }

        override fun onMessage(webSocket: WebSocket, bytes: ByteString) {
            if (current()) {
                receiver.chunk(bytes.toByteArray())
            }
        }

        override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
            webSocket.close(code, reason)
        }

        override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
            if (!current()) {
                return
            }
            socket = null
            receiver.abortAll("connection closed")
            if (isManualDisconnect || code == 1000 || code == 1001) {
                isManualDisconnect = true
                stopped.set(true)
                running.set(false)
                discovery.stop()
                setStatus("Disconnected")
                return
            }
            if (code == 1008) {
                isManualDisconnect = true
                stopped.set(true)
                running.set(false)
                discovery.stop()
                ClipboardDebugLog.record("sync", "pairing rejected")
                setStatus("Disconnected")
                return
            }
            discovery.start()
            scheduleReconnect()
        }

        override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
            if (!current()) {
                return
            }
            socket = null
            receiver.abortAll("connection failed")
            if (isManualDisconnect) {
                return
            }
            Log.w(TAG, "WebSocket failure: ${t.message}")
            discovery.start()
            scheduleReconnect()
        }
    }

    private fun write(current: WebSocket, clip: OutgoingClip): Boolean {
        val record = pairing.load() ?: return false
        val prepared = prepare(clip) ?: return false
        if (EchoGuard.wasAppliedFromPeer(prepared.hash)) {
            if (pending === clip) {
                pending = null
            }
            return true
        }
        val encrypted = EncryptionUtil.encrypt(EncryptionUtil.deriveKey(record.token), prepared.plain)
        val json = JSONObject()
            .put("version", 1)
            .put("type", prepared.type)
            .put("originDeviceId", pairing.deviceId)
            .put("deviceName", DeviceLabel.display(appContext))
            .put("contentHash", prepared.hash)
            .put("timestamp", Instant.now().toString())
            .put("payload", EncryptionUtil.b64(encrypted.ciphertext))
            .put(
                "encryption",
                JSONObject()
                    .put("iv", EncryptionUtil.b64(encrypted.iv))
                    .put("authTag", EncryptionUtil.b64(encrypted.tag)),
            )
        val sent = booleanArrayOf(false)
        withNetwork { sent[0] = current.send(json.toString()) }
        if (sent[0]) {
            EchoGuard.noteSent(prepared.hash)
            when (clip) {
                is OutgoingClip.Text -> SyncHistory.addText("Sent", clip.text)
                is OutgoingClip.Image -> ImageCodec.toJpeg(clip.encoded)?.let { SyncHistory.addImage("Sent", it) }
                is OutgoingClip.Attachment -> Unit
            }
            if (pending === clip) {
                pending = null
            }
        }
        return sent[0]
    }

    private fun prepare(clip: OutgoingClip): Prepared? {
        return when (clip) {
            is OutgoingClip.Text -> {
                val plain = clip.text.toByteArray(Charsets.UTF_8)
                Prepared("text", plain, EncryptionUtil.sha256Hex(plain))
            }
            is OutgoingClip.Image -> {
                if (!settings.syncImages) {
                    SyncHistory.addText("Skipped", "Images are off")
                    if (pending === clip) {
                        pending = null
                    }
                    return null
                }
                val jpeg = ImageCodec.toJpeg(clip.encoded)
                if (jpeg == null) {
                    SyncHistory.addText("Skipped", "Image was too large")
                    ClipboardDebugLog.record("sync", "image skipped")
                    if (pending === clip) {
                        pending = null
                    }
                    return null
                }
                val plain = Base64.encodeToString(jpeg, Base64.NO_WRAP).toByteArray(Charsets.UTF_8)
                Prepared("image", plain, EncryptionUtil.sha256Hex(plain))
            }
            is OutgoingClip.Attachment -> null
        }
    }

    private data class Prepared(val type: String, val plain: ByteArray, val hash: String)

    private fun handleMessage(text: String) {
        val record = pairing.load() ?: return
        try {
            val obj = JSONObject(text)
            if (obj.optInt("version") != 1) {
                ClipboardDebugLog.record("sync", "ignored unknown version")
                return
            }
            val type = obj.optString("type")
            if (FileProtocol.isFileType(type)) {
                handleFileMessage(obj, type, record.token)
                return
            }
            if (type != "text" && type != "image") {
                ClipboardDebugLog.record("sync", "ignored type $type")
                return
            }
            if (type == "image" && !settings.syncImages) {
                SyncHistory.addText("Skipped", "Images are off")
                return
            }
            val hash = obj.getString("contentHash")
            if (EchoGuard.isEchoOfLocalSend(hash)) {
                return
            }
            val encryption = obj.getJSONObject("encryption")
            val plain = EncryptionUtil.decrypt(
                EncryptionUtil.deriveKey(record.token),
                EncryptionUtil.b64Decode(encryption.getString("iv")),
                EncryptionUtil.b64Decode(obj.getString("payload")),
                EncryptionUtil.b64Decode(encryption.getString("authTag")),
            )
            if (EncryptionUtil.sha256Hex(plain) != hash) {
                ClipboardDebugLog.record("sync", "dropped message: hash mismatch")
                return
            }
            if (plain.size > 8 * 1024 * 1024) {
                ClipboardDebugLog.record("sync", "dropped oversized payload")
                return
            }
            EchoGuard.noteApplied(hash)
            val from = obj.optString("deviceName").ifBlank { "PC" }
            if (type == "text") {
                val decoded = plain.toString(Charsets.UTF_8)
                ClipboardReceiverService.applyText(appContext, decoded)
                SyncHistory.addText("From $from", decoded)
                ClipboardDebugLog.record("pc", "applied text len=${decoded.length}")
            } else {
                val jpeg = Base64.decode(plain, Base64.DEFAULT)
                ClipboardReceiverService.applyImage(appContext, jpeg)
                SyncHistory.addImage("From $from", jpeg)
                ClipboardDebugLog.record("pc", "applied image bytes=${jpeg.size}")
            }
        } catch (ex: Exception) {
            Log.w(TAG, "Bad payload", ex)
            ClipboardDebugLog.record("sync", "dropped message: ${ex.message}")
        }
    }

    private fun enqueueFile(file: OutgoingClip.Attachment) {
        val idBytes = ByteArray(FileProtocol.ID_SIZE).also { SecureRandom().nextBytes(it) }
        val id = FileProtocol.hex(idBytes)
        FileTransfers.begin(FileTransfer(id, file.name, sending = true, totalBytes = file.size))
        if (socket == null && !isManualDisconnect) {
            start()
        }
        fileExecutor.execute { sendFileBlocking(id, idBytes, file) }
    }

    /**
     * Streams one file. Only [FileProtocol.CHUNK_SIZE] bytes are held at a time, and
     * OkHttp's outgoing queue is kept small because it closes the socket past 16 MiB.
     */
    private fun sendFileBlocking(id: String, idBytes: ByteArray, file: OutgoingClip.Attachment) {
        holdTransferLocks(true)
        var ws: WebSocket? = null
        try {
            val open = awaitConnected() ?: throw IOException("PC is not connected")
            ws = open
            val record = pairing.load() ?: throw IOException("not paired")
            val key = EncryptionUtil.deriveKey(record.token)
            val offer = JSONObject()
                .put("transferId", id)
                .put("fileName", file.name)
                .put("fileSize", file.size)
                .put("mimeType", file.mimeType)
                .put("chunkSize", FileProtocol.CHUNK_SIZE)
            if (!open.send(envelope(key, FileProtocol.TYPE_FILE, offer))) {
                throw IOException("PC disconnected")
            }
            var index = 0
            var sent = 0L
            ParcelFileDescriptor.AutoCloseInputStream(file.descriptor).use { input ->
                val buffer = ByteArray(FileProtocol.CHUNK_SIZE)
                while (true) {
                    val read = readFully(input, buffer)
                    if (read <= 0) {
                        break
                    }
                    if (FileTransfers.isCancelled(id)) {
                        throw TransferCancelled()
                    }
                    val frame = EncryptionUtil.sealChunk(key, FileProtocol.header(idBytes, index), buffer, read)
                    while (socket === open && open.queueSize() > 4L * FileProtocol.CHUNK_SIZE) {
                        Thread.sleep(5)
                    }
                    if (socket !== open || !open.send(frame.toByteString())) {
                        throw IOException("PC disconnected")
                    }
                    index += 1
                    sent += read
                    FileTransfers.progress(id, sent, file.size)
                    if (read < buffer.size) {
                        break
                    }
                }
            }
            if (sent != file.size) {
                throw IOException("the file changed while it was being sent")
            }
            val end = JSONObject().put("transferId", id).put("chunks", index).put("fileSize", sent)
            open.send(envelope(key, FileProtocol.TYPE_END, end))
            SyncHistory.addFile("Sent", file.name, file.size, file.mimeType, null)
            ClipboardDebugLog.record("file", "sent ${file.name} bytes=$sent")
            toast("${file.name} sent to PC")
        } catch (_: TransferCancelled) {
            ws?.let { sendCancel(it, id, "cancelled") }
            SyncHistory.addText("Skipped", "Cancelled ${file.name}")
            toast("File transfer cancelled")
        } catch (ex: Exception) {
            ws?.let { sendCancel(it, id, "failed") }
            ClipboardDebugLog.record("file", "send failed for ${file.name}: ${ex.message}")
            SyncHistory.addText("Skipped", "Could not send ${file.name}")
            toast("Could not send ${file.name}")
        } finally {
            runCatching { file.descriptor.close() }
            FileTransfers.end(id)
            holdTransferLocks(false)
        }
    }

    private class TransferCancelled : Exception()

    private fun awaitConnected(): WebSocket? {
        val deadline = SystemClock.elapsedRealtime() + 30_000
        while (SystemClock.elapsedRealtime() < deadline) {
            val ws = socket
            if (ws != null && status == "Successfully connected") {
                return ws
            }
            if (isManualDisconnect) {
                return null
            }
            Thread.sleep(100)
        }
        return null
    }

    private fun readFully(input: InputStream, buffer: ByteArray): Int {
        var total = 0
        while (total < buffer.size) {
            val read = input.read(buffer, total, buffer.size - total)
            if (read < 0) {
                break
            }
            total += read
        }
        return total
    }

    private fun handleFileMessage(obj: JSONObject, type: String, token: String) {
        val key = EncryptionUtil.deriveKey(token)
        val encryption = obj.getJSONObject("encryption")
        val plain = EncryptionUtil.decrypt(
            key,
            EncryptionUtil.b64Decode(encryption.getString("iv")),
            EncryptionUtil.b64Decode(obj.getString("payload")),
            EncryptionUtil.b64Decode(encryption.getString("authTag")),
        )
        if (EncryptionUtil.sha256Hex(plain) != obj.getString("contentHash")) {
            ClipboardDebugLog.record("file", "dropped $type: hash mismatch")
            return
        }
        val meta = JSONObject(plain.toString(Charsets.UTF_8))
        when (type) {
            FileProtocol.TYPE_FILE -> receiver.begin(meta, key)
            FileProtocol.TYPE_END -> receiver.end(meta)
            FileProtocol.TYPE_CANCEL -> {
                receiver.cancel(meta)
                FileTransfers.cancel(meta.getString("transferId"))
            }
        }
    }

    private fun sendCancel(ws: WebSocket, id: String, reason: String) {
        val record = pairing.load() ?: return
        val body = JSONObject().put("transferId", id).put("reason", reason)
        runCatching { ws.send(envelope(EncryptionUtil.deriveKey(record.token), FileProtocol.TYPE_CANCEL, body)) }
    }

    private fun envelope(key: ByteArray, type: String, body: JSONObject): String {
        val plain = body.toString().toByteArray(Charsets.UTF_8)
        val encrypted = EncryptionUtil.encrypt(key, plain)
        return JSONObject()
            .put("version", 1)
            .put("type", type)
            .put("originDeviceId", pairing.deviceId)
            .put("deviceName", DeviceLabel.display(appContext))
            .put("contentHash", EncryptionUtil.sha256Hex(plain))
            .put("timestamp", Instant.now().toString())
            .put("payload", EncryptionUtil.b64(encrypted.ciphertext))
            .put(
                "encryption",
                JSONObject()
                    .put("iv", EncryptionUtil.b64(encrypted.iv))
                    .put("authTag", EncryptionUtil.b64(encrypted.tag)),
            )
            .toString()
    }

    /** The 15 s network wake lock is too short for a large file; this one lasts for the transfer. */
    private fun holdTransferLocks(hold: Boolean) {
        if (hold) {
            transferWakeLock.acquire(60 * 60 * 1000L)
        } else if (transferWakeLock.isHeld) {
            transferWakeLock.release()
        }
    }

    private fun toast(text: String) {
        android.os.Handler(android.os.Looper.getMainLooper()).post {
            android.widget.Toast.makeText(appContext, text, android.widget.Toast.LENGTH_SHORT).show()
        }
    }

    companion object {
        private const val TAG = "ClipBoardSync"
    }
}

object SyncController {
    private var client: SyncWebSocketClient? = null

    fun attach(connected: SyncWebSocketClient) {
        client = connected
        connected.start()
    }

    fun detach(connected: SyncWebSocketClient?) {
        if (client === connected) {
            client = null
        }
    }

    fun ensureStarted(context: Context) {
        com.david.clipboardsync.service.ClipboardForegroundService.start(context)
    }

    fun onPaired(context: Context) {
        ensureStarted(context)
        client?.reconnectNow()
    }

    fun disconnect(context: Context) {
        PairingManager(context.applicationContext).clear()
        client?.stop()
        ClipboardRepository.setLinkStatus("Disconnected")
    }

    fun disconnectLink() {
        client?.stop()
        ClipboardRepository.setLinkStatus("Disconnected")
    }

    fun status(): String = client?.status ?: "Not paired"

    fun loadPairing(context: Context): PairingRecord? =
        PairingManager(context.applicationContext).load()
}
