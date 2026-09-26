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

    /**
     * A file streamed in chunks. The descriptor is opened while the picker or share
     * grant is still valid; an open descriptor keeps working after the grant ends.
     */
    class Attachment(
        val name: String,
        val size: Long,
        val mimeType: String,
        val descriptor: android.os.ParcelFileDescriptor,
    ) : OutgoingClip()
}

/**
 * Copies are queued by the Quick Settings tile, the share sheet, or Send file.
 * [Channel.trySend] does not suspend.
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
