package com.david.clipboardsync

import java.util.ArrayDeque

/** Same echo rule as the Windows server. See EchoGuard.cs. */
object EchoGuard {
    private val sent = ArrayDeque<String>()
    private val applied = ArrayDeque<String>()

    @Synchronized
    fun noteSent(hash: String) = remember(sent, hash)

    @Synchronized
    fun noteApplied(hash: String) = remember(applied, hash)

    @Synchronized
    fun isEchoOfLocalSend(hash: String) = sent.contains(hash)

    @Synchronized
    fun wasAppliedFromPeer(hash: String) = applied.contains(hash)

    private fun remember(queue: ArrayDeque<String>, hash: String) {
        queue.addLast(hash)
        while (queue.size > 8) {
            queue.removeFirst()
        }
    }
}
