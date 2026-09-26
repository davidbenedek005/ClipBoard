package com.david.clipboardsync.network

import java.nio.ByteBuffer

/**
 * File transfer over the existing WebSocket. A `file` JSON message carries the
 * encrypted metadata, binary frames carry the chunks, and `file-end` or
 * `file-cancel` closes the transfer. Matches Windows FileChunkCodec.
 */
object FileProtocol {
    const val TYPE_FILE = "file"
    const val TYPE_END = "file-end"
    const val TYPE_CANCEL = "file-cancel"

    const val CHUNK_SIZE = 256 * 1024
    const val ID_SIZE = 16
    const val HEADER_SIZE = ID_SIZE + 4
    const val OVERHEAD = HEADER_SIZE + 12 + 16

    fun isFileType(type: String) = type == TYPE_FILE || type == TYPE_END || type == TYPE_CANCEL

    fun header(transferId: ByteArray, index: Int): ByteArray =
        ByteBuffer.allocate(HEADER_SIZE).put(transferId).putInt(index).array()

    fun transferIdHex(frame: ByteArray): String =
        frame.copyOfRange(0, ID_SIZE).joinToString("") { "%02x".format(it) }

    fun index(frame: ByteArray): Int = ByteBuffer.wrap(frame, ID_SIZE, 4).int

    fun hex(bytes: ByteArray): String = bytes.joinToString("") { "%02x".format(it) }
}
