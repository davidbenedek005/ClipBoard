package com.david.clipboardsync.network

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import org.json.JSONObject
import java.util.UUID

data class PairingRecord(
    val ip: String,
    val port: Int,
    val token: String,
    val pcDeviceId: String?,
)

class PairingManager(context: Context) {
    private val appContext = context.applicationContext
    private val prefs: SharedPreferences = createPrefs(appContext)

    val deviceId: String
        get() {
            val existing = prefs.getString(KEY_DEVICE, null)
            if (existing != null) {
                return existing
            }
            val created = UUID.randomUUID().toString()
            prefs.edit().putString(KEY_DEVICE, created).apply()
            return created
        }

    fun load(): PairingRecord? {
        val ip = prefs.getString(KEY_IP, null) ?: return null
        val token = prefs.getString(KEY_TOKEN, null) ?: return null
        val port = prefs.getInt(KEY_PORT, 0)
        if (port <= 0) {
            return null
        }
        return PairingRecord(ip, port, token, prefs.getString(KEY_PC, null))
    }

    fun saveManual(ip: String, port: Int, token: String, pcDeviceId: String? = null): PairingRecord {
        val record = PairingRecord(ip.trim(), port, token.trim(), pcDeviceId?.trim()?.ifBlank { null })
        if (record.ip.isBlank() || record.token.isBlank() || record.port !in 1..65535) {
            throw IllegalArgumentException("IP, port, or token is missing")
        }
        prefs.edit()
            .putString(KEY_IP, record.ip)
            .putInt(KEY_PORT, record.port)
            .putString(KEY_TOKEN, record.token)
            .putString(KEY_PC, record.pcDeviceId)
            .apply()
        return record
    }

    fun updateHost(ip: String, port: Int) {
        prefs.edit().putString(KEY_IP, ip).putInt(KEY_PORT, port).apply()
    }

    fun saveFromQr(raw: String): PairingRecord {
        val trimmed = raw.trim()
        if (trimmed.startsWith("clipboardsync://")) {
            val uri = android.net.Uri.parse(trimmed)
            return saveManual(
                uri.getQueryParameter("ip").orEmpty(),
                uri.getQueryParameter("port")?.toIntOrNull() ?: 0,
                uri.getQueryParameter("token").orEmpty(),
                uri.getQueryParameter("device"),
            )
        }
        val obj = JSONObject(trimmed)
        val record = PairingRecord(
            ip = obj.getString("ip"),
            port = obj.getInt("port"),
            token = obj.getString("token"),
            pcDeviceId = obj.optString("device").ifBlank { null },
        )
        if (record.ip.isBlank() || record.token.isBlank() || record.port !in 1..65535) {
            throw IllegalArgumentException("QR is missing ip, port, or token")
        }
        prefs.edit()
            .putString(KEY_IP, record.ip)
            .putInt(KEY_PORT, record.port)
            .putString(KEY_TOKEN, record.token)
            .putString(KEY_PC, record.pcDeviceId)
            .apply()
        return record
    }

    fun clear() {
        prefs.edit().remove(KEY_IP).remove(KEY_PORT).remove(KEY_TOKEN).remove(KEY_PC).apply()
    }

    private fun createPrefs(context: Context): SharedPreferences {
        val masterKey = MasterKey.Builder(context)
            .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
            .build()
        return EncryptedSharedPreferences.create(
            context,
            "pairing",
            masterKey,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM,
        )
    }

    private companion object {
        const val KEY_DEVICE = "deviceId"
        const val KEY_IP = "ip"
        const val KEY_PORT = "port"
        const val KEY_TOKEN = "token"
        const val KEY_PC = "pcDeviceId"
    }
}
