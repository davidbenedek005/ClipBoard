package com.david.clipboardsync

import java.util.ArrayDeque

/** Same echo rule as the Windows server. See EchoGuard.cs. */
object EchoGuard {
    private val sent = ArrayDeque<String>()
    private val applied = ArrayDeque<String>()
    private var lastText: String? = null
    private var lastImageHash: String? = null

    @Synchronized
    fun noteSent(hash: String) = remember(sent, hash)

    @Synchronized
    fun noteApplied(hash: String) = remember(applied, hash)

    @Synchronized
    fun isEchoOfLocalSend(hash: String) = sent.contains(hash)

    @Synchronized
    fun wasAppliedFromPeer(hash: String) = applied.contains(hash)

    /** Call immediately before setPrimaryClip for text that arrived from the PC. */
    @Synchronized
    fun expectText(text: String) {
        lastText = normalize(text)
    }

    /** Call immediately before setPrimaryClip for an image that arrived from the PC. */
    @Synchronized
    fun expectImage(jpeg: ByteArray) {
        lastImageHash = sha256(jpeg)
    }

    /** True when this clipboard read is the echo of the last network write. Clears that memory. */
    @Synchronized
    fun consumeText(text: String): Boolean {
        val expected = lastText ?: return false
        if (normalize(text) != expected) {
            return false
        }
        lastText = null
        return true
    }

    @Synchronized
    fun consumeImage(jpeg: ByteArray): Boolean {
        val expected = lastImageHash ?: return false
        if (sha256(jpeg) != expected) {
            return false
        }
        lastImageHash = null
        return true
    }

    private fun normalize(text: String) = text.replace("\r\n", "\n").replace('\r', '\n').trimEnd('\u0000')

    private fun sha256(bytes: ByteArray): String {
        val digest = java.security.MessageDigest.getInstance("SHA-256").digest(bytes)
        return digest.joinToString("") { "%02x".format(it) }
    }

    private fun remember(queue: ArrayDeque<String>, hash: String) {
        queue.addLast(hash)
        while (queue.size > 8) {
            queue.removeFirst()
        }
    }
}
