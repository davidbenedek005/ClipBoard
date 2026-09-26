package com.david.clipboardsync.network

import android.content.Context
import android.os.Build
import android.provider.Settings

/** The name other devices show, for example "David's Phone". The header form is ASCII-only. */
object DeviceLabel {
    fun display(context: Context): String {
        val global = Settings.Global.getString(context.contentResolver, Settings.Global.DEVICE_NAME)
        val raw = global?.trim().takeUnless { it.isNullOrEmpty() } ?: Build.MODEL
        return raw.take(40)
    }

    fun header(context: Context): String =
        display(context).map { ch -> if (ch.code in 32..126) ch else '?' }.joinToString("")
}
