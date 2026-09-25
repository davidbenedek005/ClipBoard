package com.david.clipboardsync.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.david.clipboardsync.HistoryItem

@Composable
fun OnboardingScreen(
    notificationsGranted: Boolean,
    accessibilityEnabled: Boolean,
    batteryUnrestricted: Boolean,
    connectionStatus: String,
    connected: Boolean,
    history: List<HistoryItem>,
    onDisconnect: () -> Unit,
    onConnect: (ip: String, port: Int, token: String) -> Unit,
    onCopyText: (String) -> Unit,
    onCopyImage: (ByteArray) -> Unit,
    onRequestNotifications: () -> Unit,
    onOpenAccessibilitySettings: () -> Unit,
    onRequestBatteryExemption: () -> Unit,
    onOpenSettings: () -> Unit,
) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Text("ClipBoard", style = MaterialTheme.typography.headlineMedium)
        Text(
            "Send a copy from the Send to PC quick settings tile.",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Card(
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(24.dp),
            colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainer),
        ) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Text(connectionStatus, style = MaterialTheme.typography.titleMedium)
                if (connected) {
                    Button(onClick = onDisconnect, modifier = Modifier.fillMaxWidth()) {
                        Text("Disconnect from PC")
                    }
                } else {
                    ManualFields(onConnect)
                }
            }
        }
        if (!notificationsGranted || !accessibilityEnabled || !batteryUnrestricted) {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                if (!notificationsGranted) {
                    OutlinedButton(onClick = onRequestNotifications) { Text("Notifications") }
                }
                if (!accessibilityEnabled) {
                    OutlinedButton(onClick = onOpenAccessibilitySettings) { Text("Accessibility") }
                }
                if (!batteryUnrestricted) {
                    OutlinedButton(onClick = onRequestBatteryExemption) { Text("Battery") }
                }
            }
        }
        Row(modifier = Modifier.fillMaxWidth()) {
            Text(
                "Sync history",
                style = MaterialTheme.typography.titleMedium,
                modifier = Modifier.weight(1f),
            )
            OutlinedButton(onClick = onOpenSettings) { Text("Settings") }
        }
        LazyColumn(
            modifier = Modifier.weight(1f),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            if (history.isEmpty()) {
                item {
                    Text(
                        "Nothing synced yet.",
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }
            items(history) { item ->
                HistoryItemCard(item, onCopyText, onCopyImage)
            }
        }
    }
}

@Composable
private fun ManualFields(
    onConnect: (ip: String, port: Int, token: String) -> Unit,
) {
    var ip by rememberSaveable { mutableStateOf("") }
    var port by rememberSaveable { mutableStateOf("53211") }
    var token by rememberSaveable { mutableStateOf("") }
    var formError by rememberSaveable { mutableStateOf<String?>(null) }
    Text(
        "Enter the IP, port, and token from the PC, or scan the QR with the phone camera.",
        style = MaterialTheme.typography.bodySmall,
    )
    OutlinedTextField(
        value = ip,
        onValueChange = { ip = it },
        label = { Text("IP address") },
        modifier = Modifier.fillMaxWidth(),
        singleLine = true,
        shape = RoundedCornerShape(16.dp),
    )
    OutlinedTextField(
        value = port,
        onValueChange = { port = it.filter { ch -> ch.isDigit() }.take(5) },
        label = { Text("Port") },
        modifier = Modifier.fillMaxWidth(),
        singleLine = true,
        shape = RoundedCornerShape(16.dp),
    )
    OutlinedTextField(
        value = token,
        onValueChange = { token = it.trim() },
        label = { Text("Pairing token") },
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(16.dp),
    )
    formError?.let { Text(it, color = MaterialTheme.colorScheme.error) }
    Button(
        onClick = {
            val parsedPort = port.toIntOrNull()
            if (ip.isBlank() || token.isBlank() || parsedPort == null || parsedPort !in 1..65535) {
                formError = "Enter the IP, port, and token shown on the PC."
            } else {
                formError = null
                onConnect(ip.trim(), parsedPort, token.trim())
            }
        },
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(16.dp),
    ) {
        Text("Connect")
    }
}
