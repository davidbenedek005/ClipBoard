package com.david.clipboardsync

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.time.LocalTime
import java.time.format.DateTimeFormatter

sealed class HistoryItem {
    abstract val time: String
    abstract val direction: String

    data class TextItem(
        override val time: String,
        override val direction: String,
        val text: String,
    ) : HistoryItem()

    data class ImageItem(
        override val time: String,
        override val direction: String,
        val jpeg: ByteArray,
    ) : HistoryItem()
}

object SyncHistory {
    private val formatter = DateTimeFormatter.ofPattern("HH:mm")
    private val buffer = ArrayDeque<HistoryItem>()
    private val _items = MutableStateFlow<List<HistoryItem>>(emptyList())
    val items: StateFlow<List<HistoryItem>> = _items.asStateFlow()

    fun addText(direction: String, text: String) {
        push(HistoryItem.TextItem(stamp(), direction, text))
    }

    fun addImage(direction: String, jpeg: ByteArray) {
        push(HistoryItem.ImageItem(stamp(), direction, jpeg))
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
