package com.david.clipboardsync.service

import android.accessibilityservice.AccessibilityService
import android.util.Log
import android.view.accessibility.AccessibilityEvent
import com.david.clipboardsync.ClipboardDebugLog

/**
 * Kept so the existing accessibility toggle still has a service to bind.
 * Copies are not read here. The Quick Settings tile is the only send path.
 */
class ClipboardAccessibilityService : AccessibilityService() {

    override fun onServiceConnected() {
        super.onServiceConnected()
        Log.i(TAG, "Accessibility service connected.")
        ClipboardDebugLog.record("service", "accessibility connected")
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) {
    }

    override fun onInterrupt() {
    }

    override fun onDestroy() {
        Log.i(TAG, "Accessibility service destroyed.")
        ClipboardDebugLog.record("service", "accessibility disconnected")
        super.onDestroy()
    }

    companion object {
        private const val TAG = "ClipBoardA11y"
    }
}
