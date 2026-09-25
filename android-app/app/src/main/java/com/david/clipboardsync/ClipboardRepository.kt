package com.david.clipboardsync

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.receiveAsFlow

sealed class OutgoingClip {
    data class Text(val text: String) : OutgoingClip()
    data class Image(val encoded: ByteArray) : OutgoingClip()
}

/**
 * The accessibility service only enqueues a copy. On Android 14 / One UI the
 * callback itself is often delivered only after the activity is in front, so
 * this queue is allowed to sit until then. [Channel.trySend] does not suspend.
 */
object ClipboardRepository {
    private val copies = Channel<OutgoingClip>(
        capacity = 32,
        onBufferOverflow = BufferOverflow.DROP_OLDEST,
    )

    val outgoing: Flow<OutgoingClip> = copies.receiveAsFlow()

    private val link = MutableStateFlow("Disconnected")
    val linkStatus: StateFlow<String> = link.asStateFlow()

    fun publish(clip: OutgoingClip) {
        copies.trySend(clip)
    }

    fun setLinkStatus(value: String) {
        link.value = value
    }
}
