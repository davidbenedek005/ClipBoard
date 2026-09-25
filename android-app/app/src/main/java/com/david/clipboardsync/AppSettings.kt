package com.david.clipboardsync

import android.content.Context

class AppSettings(context: Context) {
    private val prefs = context.applicationContext.getSharedPreferences("settings", Context.MODE_PRIVATE)

    var syncImages: Boolean
        get() = prefs.getBoolean(KEY_IMAGES, true)
        set(value) {
            prefs.edit().putBoolean(KEY_IMAGES, value).apply()
        }

    private companion object {
        const val KEY_IMAGES = "syncImages"
    }
}
