package com.david.clipboardsync

import android.app.Activity
import android.os.Bundle
import android.widget.Toast

/**
 * Launched by the Quick Settings tile. Android 14 only returns the clipboard
 * to a focused activity, so this transparent window reads it and closes.
 */
class SendClipboardActivity : Activity() {

    private var delivered = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (publish()) {
            finish()
        }
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (!hasFocus || delivered) {
            return
        }
        if (!publish()) {
            Toast.makeText(this, "Nothing on the clipboard", Toast.LENGTH_SHORT).show()
        }
        finish()
    }

    private fun publish(): Boolean {
        if (delivered) {
            return true
        }
        delivered = ClipboardPublisher.publish(this)
        return delivered
    }
}
