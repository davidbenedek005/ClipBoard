package com.david.clipboardsync

import android.app.DownloadManager
import android.content.ActivityNotFoundException
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.provider.MediaStore
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.david.clipboardsync.network.PairingManager
import com.david.clipboardsync.network.SyncController
import com.david.clipboardsync.service.ClipboardForegroundService
import com.david.clipboardsync.service.ClipboardReceiverService
import com.david.clipboardsync.ui.MainScreen
import com.david.clipboardsync.ui.MainTab
import com.david.clipboardsync.ui.theme.ClipBoardTheme

class MainActivity : ComponentActivity() {

    private var resumeTick by mutableIntStateOf(0)
    private var selectedTab by mutableStateOf(MainTab.History)
    private var syncImages by mutableStateOf(true)

    /** The picker grant only lasts while this activity is alive, so FilePublisher opens the descriptor right away. */
    private val pickFile = registerForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) {
            FilePublisher.publish(this, uri)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            val history by SyncHistory.items.collectAsStateWithLifecycle()
            val transfers by FileTransfers.active.collectAsStateWithLifecycle()
            val linkStatus by ClipboardRepository.linkStatus.collectAsStateWithLifecycle()
            val battery = remember(resumeTick) { PermissionStatus.batteryUnrestricted(this) }
            val paired = remember(resumeTick) { SyncController.loadPairing(this) != null }
            ClipBoardTheme {
                MainScreen(
                    selectedTab = selectedTab,
                    onSelectTab = { selectedTab = it },
                    connected = linkStatus == "Successfully connected",
                    paired = paired,
                    history = history,
                    syncImages = syncImages,
                    batteryUnrestricted = battery,
                    onCopyText = { text -> ClipboardReceiverService.applyText(this@MainActivity, text) },
                    onCopyImage = { jpeg -> ClipboardReceiverService.applyImage(this@MainActivity, jpeg) },
                    onDeleteItem = SyncHistory::remove,
                    onClearHistory = SyncHistory::clear,
                    transfers = transfers,
                    onSendFile = { pickFile.launch(arrayOf("*/*")) },
                    onCancelTransfer = FileTransfers::cancel,
                    onOpenFile = ::openReceivedFile,
                    onOpenDownloads = ::openDownloads,
                    onScanQr = ::openCamera,
                    onConnect = ::onManualPairing,
                    onDisconnect = { SyncController.disconnectLink() },
                    onSyncImages = {
                        syncImages = it
                        AppSettings(this@MainActivity).syncImages = it
                    },
                    onRequestBatteryExemption = ::requestBatteryExemption,
                    onForgetPc = {
                        SyncController.disconnect(this@MainActivity)
                        resumeTick++
                    },
                )
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
        // POST_NOTIFICATIONS is never requested. Without it Android 13+ still runs the
        // foreground service and only hides its notification.
        if (SyncController.loadPairing(this) != null) {
            ClipboardForegroundService.start(this)
        }
        SyncController.ensureStarted(this)
    }

    private fun handlePairingIntent(intent: Intent?) {
        val data = intent?.data ?: return
        if (data.scheme != "clipboardsync" || data.host != "pair") {
            return
        }
        selectedTab = MainTab.Pairing
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
        try {
            PairingManager(this).saveManual(ip, port, token)
            SyncController.onPaired(this)
            Toast.makeText(this, "Paired. Waiting for the PC.", Toast.LENGTH_SHORT).show()
        } catch (ex: Exception) {
            Toast.makeText(this, "Check the IP, port, and token from the PC.", Toast.LENGTH_LONG).show()
        }
        resumeTick++
    }

    /** The system camera reads the PC's QR and opens the clipboardsync:// deep link back into this activity. */
    private fun openCamera() {
        try {
            startActivity(Intent(MediaStore.INTENT_ACTION_STILL_IMAGE_CAMERA))
        } catch (_: ActivityNotFoundException) {
            Toast.makeText(this, "Open the camera app and scan the QR code on the PC.", Toast.LENGTH_LONG).show()
        }
    }

    private fun openReceivedFile(item: HistoryItem.FileItem) {
        val uri = item.uri?.let(Uri::parse) ?: return
        val view = Intent(Intent.ACTION_VIEW)
            .setDataAndType(uri, item.mimeType)
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        try {
            startActivity(view)
        } catch (_: ActivityNotFoundException) {
            Toast.makeText(this, "No app on this phone can open ${item.fileName}", Toast.LENGTH_SHORT).show()
        }
    }

    private fun openDownloads() {
        try {
            startActivity(Intent(DownloadManager.ACTION_VIEW_DOWNLOADS))
        } catch (_: ActivityNotFoundException) {
            Toast.makeText(this, "Open the Files app to find Downloads", Toast.LENGTH_SHORT).show()
        }
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
