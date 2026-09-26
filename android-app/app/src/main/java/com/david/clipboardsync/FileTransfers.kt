package com.david.clipboardsync

import android.os.SystemClock
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import java.util.concurrent.ConcurrentHashMap

data class FileTransfer(
    val id: String,
    val fileName: String,
    val sending: Boolean,
    val totalBytes: Long,
    val bytesDone: Long = 0,
) {
    val fraction: Float
        get() = if (totalBytes <= 0) 1f else (bytesDone.toFloat() / totalBytes).coerceIn(0f, 1f)
}

/** Active transfers for the progress cards. Updated from network threads, throttled to about ten a second. */
object FileTransfers {
    private val _active = MutableStateFlow<List<FileTransfer>>(emptyList())
    val active: StateFlow<List<FileTransfer>> = _active.asStateFlow()

    private val lastReport = ConcurrentHashMap<String, Long>()
    private val cancelled = ConcurrentHashMap.newKeySet<String>()

    fun begin(transfer: FileTransfer) {
        _active.update { it + transfer }
    }

    fun progress(id: String, bytesDone: Long, totalBytes: Long) {
        val now = SystemClock.elapsedRealtime()
        val last = lastReport[id] ?: 0L
        if (bytesDone < totalBytes && now - last < 100) {
            return
        }
        lastReport[id] = now
        _active.update { list -> list.map { if (it.id == id) it.copy(bytesDone = bytesDone) else it } }
    }

    fun end(id: String) {
        lastReport.remove(id)
        cancelled.remove(id)
        _active.update { list -> list.filterNot { it.id == id } }
    }

    fun cancel(id: String) {
        cancelled.add(id)
    }

    fun isCancelled(id: String) = id in cancelled
}
