package com.david.clipboardsync.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.WindowInsetsSides
import androidx.compose.foundation.layout.consumeWindowInsets
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.only
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.List
import androidx.compose.material.icons.filled.Phone
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.ExtendedFloatingActionButton
import com.david.clipboardsync.FileTransfer
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.NavigationBarItemDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.david.clipboardsync.HistoryItem
import com.david.clipboardsync.ui.theme.ConnectedGreen

enum class MainTab(val label: String, val title: String, val icon: ImageVector) {
    History("History", "Sync history", Icons.AutoMirrored.Filled.List),
    Pairing("Pairing", "Pairing", Icons.Filled.Phone),
    Settings("Settings", "Settings", Icons.Filled.Settings),
}

@Composable
fun MainScreen(
    selectedTab: MainTab,
    onSelectTab: (MainTab) -> Unit,
    connected: Boolean,
    paired: Boolean,
    history: List<HistoryItem>,
    syncImages: Boolean,
    batteryUnrestricted: Boolean,
    onCopyText: (String) -> Unit,
    onCopyImage: (ByteArray) -> Unit,
    onDeleteItem: (Long) -> Unit,
    onClearHistory: () -> Unit,
    transfers: List<FileTransfer>,
    onSendFile: () -> Unit,
    onCancelTransfer: (String) -> Unit,
    onOpenFile: (HistoryItem.FileItem) -> Unit,
    onOpenDownloads: () -> Unit,
    onScanQr: () -> Unit,
    onConnect: (ip: String, port: Int, token: String) -> Unit,
    onDisconnect: () -> Unit,
    onSyncImages: (Boolean) -> Unit,
    onRequestBatteryExemption: () -> Unit,
    onForgetPc: () -> Unit,
) {
    var confirmClear by rememberSaveable { mutableStateOf(false) }
    if (confirmClear) {
        AlertDialog(
            onDismissRequest = { confirmClear = false },
            title = { Text("Clear sync history?") },
            text = { Text("This removes every item from the list on this phone. The PC keeps its own history.") },
            confirmButton = {
                TextButton(onClick = {
                    confirmClear = false
                    onClearHistory()
                }) { Text("Clear") }
            },
            dismissButton = {
                TextButton(onClick = { confirmClear = false }) { Text("Cancel") }
            },
        )
    }

    Scaffold(
        containerColor = MaterialTheme.colorScheme.background,
        topBar = {
            Header(selectedTab.title, connected) {
                if (selectedTab == MainTab.History && history.isNotEmpty()) {
                    IconButton(onClick = { confirmClear = true }) {
                        Icon(
                            Icons.Outlined.Delete,
                            contentDescription = "Clear sync history",
                            tint = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
            }
        },
        floatingActionButton = {
            if (selectedTab == MainTab.History) {
                ExtendedFloatingActionButton(
                    onClick = onSendFile,
                    icon = { Icon(Icons.Filled.Add, contentDescription = null) },
                    text = { Text("Send file") },
                    containerColor = MaterialTheme.colorScheme.primary,
                    contentColor = MaterialTheme.colorScheme.onPrimary,
                )
            }
        },
        bottomBar = {
            NavigationBar(containerColor = MaterialTheme.colorScheme.surfaceContainerLow) {
                MainTab.entries.forEach { tab ->
                    NavigationBarItem(
                        selected = tab == selectedTab,
                        onClick = { onSelectTab(tab) },
                        icon = { Icon(tab.icon, contentDescription = null) },
                        label = { Text(tab.label) },
                        colors = NavigationBarItemDefaults.colors(
                            selectedIconColor = MaterialTheme.colorScheme.onPrimary,
                            indicatorColor = MaterialTheme.colorScheme.primary,
                            selectedTextColor = MaterialTheme.colorScheme.primary,
                        ),
                    )
                }
            }
        },
    ) { innerPadding ->
        val content = Modifier
            .padding(innerPadding)
            .consumeWindowInsets(innerPadding)
        when (selectedTab) {
            MainTab.History -> HistoryScreen(
                history = history,
                needsBatteryExemption = !batteryUnrestricted,
                onAllowBattery = onRequestBatteryExemption,
                onCopyText = onCopyText,
                onCopyImage = onCopyImage,
                onDelete = onDeleteItem,
                transfers = transfers,
                onCancelTransfer = onCancelTransfer,
                onOpenFile = onOpenFile,
                onOpenDownloads = onOpenDownloads,
                modifier = content,
            )
            MainTab.Pairing -> PairingScreen(
                connected = connected,
                paired = paired,
                onScanQr = onScanQr,
                onConnect = onConnect,
                onDisconnect = onDisconnect,
                modifier = content,
            )
            MainTab.Settings -> SettingsScreen(
                syncImages = syncImages,
                paired = paired,
                batteryUnrestricted = batteryUnrestricted,
                onSyncImages = onSyncImages,
                onRequestBatteryExemption = onRequestBatteryExemption,
                onForgetPc = onForgetPc,
                modifier = content,
            )
        }
    }
}

@Composable
private fun Header(
    title: String,
    connected: Boolean,
    action: @Composable () -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Top + WindowInsetsSides.Horizontal))
            .padding(start = ScreenPadding + 4.dp, end = ScreenPadding, top = 20.dp, bottom = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(
                "ClipBoard",
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.primary,
            )
            Text(
                title,
                style = MaterialTheme.typography.headlineMedium,
                color = MaterialTheme.colorScheme.onBackground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        Spacer(Modifier.width(12.dp))
        StatusPill(connected)
        action()
    }
}

@Composable
private fun StatusPill(connected: Boolean) {
    Surface(
        shape = RoundedCornerShape(50),
        color = MaterialTheme.colorScheme.surfaceContainerHigh,
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Surface(
                modifier = Modifier.size(8.dp),
                shape = CircleShape,
                color = if (connected) ConnectedGreen else MaterialTheme.colorScheme.outline,
            ) {}
            Spacer(Modifier.width(8.dp))
            Text(
                if (connected) "Connected" else "Disconnected",
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurface,
            )
        }
    }
}
