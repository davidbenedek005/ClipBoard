package com.david.clipboardsync

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter

/**
 * In-process log of clipboard observations. Logcat is the source of truth while the
 * activity is dead; this flow is what the debug UI shows when you reopen the app.
 * Entries are previews only. They can still contain passwords.
 */
object ClipboardDebugLog {
    private val timeFormat = DateTimeFormatter.ofPattern("HH:mm:ss")
    private val _entries = MutableStateFlow<List<String>>(emptyList())
    val entries: StateFlow<List<String>> = _entries.asStateFlow()

    fun record(source: String, summary: String) {
        val clock = timeFormat.format(Instant.now().atZone(ZoneId.systemDefault()))
        val line = "$clock  $source  $summary"
        _entries.value = listOf(line) + _entries.value.take(49)
    }

    fun clear() {
        _entries.value = emptyList()
    }
}
