package com.david.clipboardsync.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.PowerManager
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import com.david.clipboardsync.ClipboardRepository
import com.david.clipboardsync.MainActivity
import com.david.clipboardsync.R
import com.david.clipboardsync.network.PairingManager
import com.david.clipboardsync.network.SyncController
import com.david.clipboardsync.network.SyncWebSocketClient
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

/**
 * Owns the WebSocket for the life of the process. Clipboard bytes are sent only
 * when the user taps the Quick Settings tile.
 *
 * A short partial wake lock covers the connect and send so One UI does not defer
 * the radio until the user opens the activity. The lock is released immediately
 * afterward; the ongoing notification is what keeps the process eligible to run.
 */
class ClipboardForegroundService : android.app.Service() {

    override fun onCreate() {
        super.onCreate()
        running = this
        createChannel()
        startAsForeground()
        if (client == null) {
            client = SyncWebSocketClient(applicationContext, PairingManager(applicationContext), ::holdWakeLock)
            SyncController.attach(client!!)
        }
        ensureCollecting()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        startAsForeground()
        return START_STICKY
    }

    override fun onDestroy() {
        SyncController.detach(client)
        client?.stop()
        client = null
        if (running === this) {
            running = null
        }
        super.onDestroy()
    }

    override fun onBind(intent: Intent?) = null

    private fun startAsForeground() {
        val notification = buildNotification(lastPreview)
        notification.flags = notification.flags or
            Notification.FLAG_ONGOING_EVENT or
            Notification.FLAG_NO_CLEAR
        val type = if (Build.VERSION.SDK_INT >= 29) {
            ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC
        } else {
            0
        }
        ServiceCompat.startForeground(this, NOTIFICATION_ID, notification, type)
    }

    private fun holdWakeLock(block: () -> Unit) {
        val power = getSystemService(PowerManager::class.java)
        val wakeLock = power.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "ClipBoard:sync")
        wakeLock.acquire(15_000L)
        try {
            block()
        } finally {
            if (wakeLock.isHeld) {
                wakeLock.release()
            }
        }
    }

    private fun createChannel() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) {
            return
        }
        val manager = getSystemService(NotificationManager::class.java)
        val channel = NotificationChannel(
            CHANNEL_ID,
            getString(R.string.notification_channel_name),
            NotificationManager.IMPORTANCE_LOW,
        ).apply {
            description = getString(R.string.notification_channel_description)
            setSound(null, null)
        }
        manager.createNotificationChannel(channel)
    }

    private fun buildNotification(body: String): Notification {
        val open = PendingIntent.getActivity(
            this,
            0,
            Intent(this, MainActivity::class.java),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle("ClipBoard")
            .setContentText(body)
            .setOngoing(true)
            .setAutoCancel(false)
            .setOnlyAlertOnce(true)
            .setForegroundServiceBehavior(NotificationCompat.FOREGROUND_SERVICE_IMMEDIATE)
            .setContentIntent(open)
            .setCategory(NotificationCompat.CATEGORY_SERVICE)
            .build()
    }

    companion object {
        private const val CHANNEL_ID = "clipboard_sync"
        private const val NOTIFICATION_ID = 41

        @Volatile
        private var running: ClipboardForegroundService? = null

        private var client: SyncWebSocketClient? = null

        /**
         * Not an Activity lifecycleScope. This scope is created with the process
         * and is never cancelled when the activity stops or the service is
         * destroyed. [Dispatchers.IO] is not the main looper, which One UI
         * freezes while the app is backgrounded.
         */
        private val collectScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
        private val collecting = java.util.concurrent.atomic.AtomicBoolean(false)

        private fun ensureCollecting() {
            if (!collecting.compareAndSet(false, true)) {
                return
            }
            collectScope.launch {
                ClipboardRepository.outgoing.collect { clip ->
                    client?.send(clip)
                }
            }
        }

        @Volatile
        var lastPreview: String = "Disconnected"

        fun start(context: Context) {
            if (running != null) {
                return
            }
            val intent = Intent(context, ClipboardForegroundService::class.java)
            ContextCompat.startForegroundService(context, intent)
        }

        fun noteStatus(context: Context, status: String) {
            if (status != "Successfully connected" && status != "Disconnected") {
                return
            }
            if (lastPreview == status) {
                return
            }
            lastPreview = status
            show()
        }

        private fun show() {
            running?.startAsForeground()
        }
    }
}
