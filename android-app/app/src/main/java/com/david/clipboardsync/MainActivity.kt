package com.david.clipboardsync

import android.Manifest
import android.content.ActivityNotFoundException
import android.content.Intent
import android.os.Build
import android.os.Bundle
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.david.clipboardsync.network.PairingManager
import com.david.clipboardsync.network.SyncController
import com.david.clipboardsync.service.ClipboardForegroundService
import com.david.clipboardsync.service.ClipboardReceiverService
import com.david.clipboardsync.ui.ManualConnectionScreen
import com.david.clipboardsync.ui.OnboardingScreen
import com.david.clipboardsync.ui.SettingsScreen

class MainActivity : ComponentActivity() {

    private var resumeTick by mutableIntStateOf(0)
    private var showManual by mutableStateOf(false)
    private var showSettings by mutableStateOf(false)
    private var syncImages by mutableStateOf(true)

    private val notificationPermission = registerForActivityResult(
        ActivityResultContracts.RequestPermission(),
    ) { granted ->
        if (granted) {
            ClipboardForegroundService.start(this)
        }
        resumeTick++
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            val history by SyncHistory.items.collectAsStateWithLifecycle()
            val linkStatus by ClipboardRepository.linkStatus.collectAsStateWithLifecycle()
            val notifications = PermissionStatus.notificationsGranted(this)
            val accessibility = PermissionStatus.accessibilityEnabled(this)
            val battery = PermissionStatus.batteryUnrestricted(this)
            key(resumeTick) {
            MaterialTheme {
                Surface {
                    if (showSettings) {
                        SettingsScreen(
                            syncImages = syncImages,
                            paired = SyncController.loadPairing(this@MainActivity) != null,
                            onSyncImages = {
                                syncImages = it
                                AppSettings(this@MainActivity).syncImages = it
                            },
                            onDisconnect = {
                                SyncController.disconnect(this@MainActivity)
                                showSettings = false
                                resumeTick++
                            },
                            onClose = { showSettings = false },
                        )
                    } else if (showManual) {
                        ManualConnectionScreen(
                            onConnect = ::onManualPairing,
                            onClose = { showManual = false },
                        )
                    } else {
                    OnboardingScreen(
                        notificationsGranted = notifications,
                        accessibilityEnabled = accessibility,
                        batteryUnrestricted = battery,
                        connectionStatus = linkStatus,
                        connected = linkStatus == "Successfully connected",
                        history = history,
                        onDisconnect = {
                            SyncController.disconnectLink()
                        },
                        onConnect = ::onManualPairing,
                        onCopyText = { text ->
                            com.david.clipboardsync.service.ClipboardReceiverService.applyText(this@MainActivity, text)
                        },
                        onCopyImage = { jpeg ->
                            com.david.clipboardsync.service.ClipboardReceiverService.applyImage(this@MainActivity, jpeg)
                        },
                        onRequestNotifications = ::requestNotifications,
                        onOpenAccessibilitySettings = ::openAccessibilitySettings,
                        onRequestBatteryExemption = ::requestBatteryExemption,
                        onOpenSettings = { showSettings = true },
                    )
                    }
                }
            }
            }
        }
        handlePairingIntent(intent)
        syncImages = AppSettings(this).syncImages
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handlePairingIntent(intent)
    }

    override fun onResume() {
        super.onResume()
        resumeTick++
        if (PermissionStatus.accessibilityEnabled(this) &&
            PermissionStatus.notificationsGranted(this)
        ) {
            ClipboardForegroundService.start(this)
        }
        SyncController.ensureStarted(this)
    }

    private fun handlePairingIntent(intent: Intent?) {
        val data = intent?.data ?: return
        if (data.scheme != "clipboardsync" || data.host != "pair") {
            return
        }
        showManual = false
        try {
            PairingManager(this).saveFromQr(data.toString())
            SyncController.onPaired(this)
            Toast.makeText(this, "Paired. Waiting for the PC.", Toast.LENGTH_SHORT).show()
            resumeTick++
        } catch (ex: Exception) {
            Toast.makeText(this, "That link is not a ClipBoard pairing code.", Toast.LENGTH_LONG).show()
        }
    }

    private fun onManualPairing(ip: String, port: Int, token: String) {
        showManual = false
        try {
            PairingManager(this).saveManual(ip, port, token)
            SyncController.onPaired(this)
            Toast.makeText(this, "Paired. Waiting for the PC.", Toast.LENGTH_SHORT).show()
        } catch (ex: Exception) {
            Toast.makeText(this, "Check the IP, port, and token from the PC.", Toast.LENGTH_LONG).show()
        }
        resumeTick++
    }

    private fun openAccessibilitySettings() {
        val details = PermissionStatus.accessibilityDetails(this)
        if (details != null) {
            try {
                startActivity(details)
                return
            } catch (_: ActivityNotFoundException) {
                // API 33+ devices that do not ship the details screen.
            }
        }
        startActivity(PermissionStatus.accessibilitySettingsList())
    }

    private fun requestNotifications() {
        if (Build.VERSION.SDK_INT < 33) {
            Toast.makeText(this, "Notifications do not need a runtime prompt on this Android version.", Toast.LENGTH_SHORT).show()
            ClipboardForegroundService.start(this)
            return
        }
        notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
    }

    private fun requestBatteryExemption() {
        if (PermissionStatus.batteryUnrestricted(this)) {
            return
        }
        try {
            startActivity(PermissionStatus.batteryExemption(this))
        } catch (_: SecurityException) {
            // Some OEM builds refuse ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS.
            startActivity(PermissionStatus.batterySettings())
        }
    }
}
