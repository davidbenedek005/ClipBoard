package com.david.clipboardsync

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.time.LocalTime
import java.time.format.DateTimeFormatter
import java.util.concurrent.atomic.AtomicLong

sealed class HistoryItem {
    /** Unique per process. Deletion and LazyColumn keys use it, because two copies of the same text are equal. */
    abstract val id: Long
    abstract val time: String
    abstract val direction: String

    data class TextItem(
        override val id: Long,
        override val time: String,
        override val direction: String,
        val text: String,
    ) : HistoryItem()

    data class ImageItem(
        override val id: Long,
        override val time: String,
        override val direction: String,
        val jpeg: ByteArray,
    ) : HistoryItem()

    /** [uri] is set for received files (MediaStore or FileProvider); sent files keep no reference. */
    data class FileItem(
        override val id: Long,
        override val time: String,
        override val direction: String,
        val fileName: String,
        val size: Long,
        val mimeType: String,
        val uri: String?,
    ) : HistoryItem()
}

object SyncHistory {
    private val formatter = DateTimeFormatter.ofPattern("HH:mm")
    private val nextId = AtomicLong()
    private val buffer = ArrayDeque<HistoryItem>()
    private val _items = MutableStateFlow<List<HistoryItem>>(emptyList())
    val items: StateFlow<List<HistoryItem>> = _items.asStateFlow()

    fun addText(direction: String, text: String) {
        push(HistoryItem.TextItem(nextId.incrementAndGet(), stamp(), direction, text))
    }

    fun addImage(direction: String, jpeg: ByteArray) {
        push(HistoryItem.ImageItem(nextId.incrementAndGet(), stamp(), direction, jpeg))
    }

    fun addFile(direction: String, fileName: String, size: Long, mimeType: String, uri: String?) {
        push(HistoryItem.FileItem(nextId.incrementAndGet(), stamp(), direction, fileName, size, mimeType, uri))
    }

    fun remove(id: Long) {
        synchronized(buffer) {
            if (buffer.removeAll { it.id == id }) {
                _items.value = buffer.toList()
            }
        }
    }

    fun clear() {
        synchronized(buffer) {
            buffer.clear()
            _items.value = emptyList()
        }
    }

    private fun stamp(): String = LocalTime.now().format(formatter)

    private fun push(item: HistoryItem) {
        synchronized(buffer) {
            buffer.addFirst(item)
            while (buffer.size > 20) {
                buffer.removeLast()
            }
            _items.value = buffer.toList()
        }
    }
}
