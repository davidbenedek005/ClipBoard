package com.david.clipboardsync

import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.PowerManager
import android.provider.Settings
import com.david.clipboardsync.service.ClipboardAccessibilityService

internal object PermissionStatus {

    private const val ACTION_ACCESSIBILITY_DETAILS_SETTINGS =
        "android.settings.ACCESSIBILITY_DETAILS_SETTINGS"

    /**
     * Accessibility cannot be enabled from code. The secure setting is a colon-separated
     * list of flattened [ComponentName]s. Match our service exactly.
     */
    fun accessibilityEnabled(context: Context): Boolean {
        val expected = ComponentName(context, ClipboardAccessibilityService::class.java)
            .flattenToString()
        val enabled = Settings.Secure.getString(
            context.contentResolver,
            Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES,
        ) ?: return false
        return enabled.split(':').any { it.equals(expected, ignoreCase = true) }
    }

    fun batteryUnrestricted(context: Context): Boolean {
        val power = context.getSystemService(PowerManager::class.java)
        return power.isIgnoringBatteryOptimizations(context.packageName)
    }

    /**
     * API 33+ opens the toggle for this service. The action string is the platform
     * value of the API 33 settings intent. Referencing it as a [Settings] field fails
     * to compile against some SDK 35 stubs that do not declare that constant.
     *
     * Older releases only have the full accessibility list
     * ([Settings.ACTION_ACCESSIBILITY_SETTINGS]). Callers should start
     * [accessibilityDetails] and, on [android.content.ActivityNotFoundException],
     * start [accessibilitySettingsList].
     */
    fun accessibilityDetails(context: Context): Intent? {
        if (Build.VERSION.SDK_INT < 33) {
            return null
        }
        return Intent(ACTION_ACCESSIBILITY_DETAILS_SETTINGS).apply {
            putExtra(
                Intent.EXTRA_COMPONENT_NAME,
                ComponentName(context, ClipboardAccessibilityService::class.java).flattenToString(),
            )
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        }
    }

    fun accessibilitySettingsList(): Intent =
        Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)

    fun batteryExemption(context: Context): Intent =
        Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS).apply {
            data = Uri.parse("package:${context.packageName}")
        }

    /** Samsung and some other OEMs reject the direct request. This opens the full list. */
    fun batterySettings(): Intent =
        Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
}
