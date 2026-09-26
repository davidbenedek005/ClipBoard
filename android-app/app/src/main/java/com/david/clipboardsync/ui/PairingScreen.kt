package com.david.clipboardsync.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.david.clipboardsync.ui.theme.ConnectedGreen

@Composable
fun PairingScreen(
    connected: Boolean,
    paired: Boolean,
    onScanQr: () -> Unit,
    onConnect: (ip: String, port: Int, token: String) -> Unit,
    onDisconnect: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(
        modifier = modifier
            .fillMaxSize()
            .imePadding()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = ScreenPadding, vertical = 16.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        SectionCard {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Surface(
                    modifier = Modifier.size(10.dp),
                    shape = CircleShape,
                    color = if (connected) ConnectedGreen else MaterialTheme.colorScheme.outline,
                ) {}
                Spacer(Modifier.width(10.dp))
                Text(
                    if (connected) "Connected to your PC" else "Not connected",
                    style = MaterialTheme.typography.titleMedium,
                )
            }
            Text(
                when {
                    connected -> "Clipboard changes sync over your local network."
                    paired -> "Paired. ClipBoard connects when the PC is reachable."
                    else -> "Pair with the PC using the QR code or the manual fields below."
                },
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            if (connected) {
                OutlinedButton(
                    onClick = onDisconnect,
                    modifier = Modifier.fillMaxWidth(),
                    shape = MaterialTheme.shapes.medium,
                ) {
                    Text("Disconnect from PC")
                }
            }
        }

        if (!connected) {
            SectionCard {
                Text("Scan QR code", style = MaterialTheme.typography.titleMedium)
                Text(
                    "Open Pairing on the PC, then point the camera at the code. The camera offers to open ClipBoard.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Button(
                    onClick = onScanQr,
                    modifier = Modifier.fillMaxWidth(),
                    shape = MaterialTheme.shapes.medium,
                ) {
                    Text("Open camera")
                }
            }

            SectionCard {
                Text("Manual connection", style = MaterialTheme.typography.titleMedium)
                Text(
                    "Type the IP address, port, and token shown on the PC.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Spacer(Modifier.height(4.dp))
                ManualConnectionForm(onConnect)
            }
        }
    }
}

@Composable
private fun ManualConnectionForm(
    onConnect: (ip: String, port: Int, token: String) -> Unit,
) {
    var ip by rememberSaveable { mutableStateOf("") }
    var port by rememberSaveable { mutableStateOf("53211") }
    var token by rememberSaveable { mutableStateOf("") }
    var formError by rememberSaveable { mutableStateOf<String?>(null) }

    OutlinedTextField(
        value = ip,
        onValueChange = { ip = it.trim() },
        label = { Text("IP address") },
        placeholder = { Text("192.168.1.20") },
        modifier = Modifier.fillMaxWidth(),
        singleLine = true,
        shape = MaterialTheme.shapes.medium,
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri, imeAction = ImeAction.Next),
    )
    OutlinedTextField(
        value = port,
        onValueChange = { port = it.filter { ch -> ch.isDigit() }.take(5) },
        label = { Text("Port") },
        modifier = Modifier.fillMaxWidth(),
        singleLine = true,
        shape = MaterialTheme.shapes.medium,
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number, imeAction = ImeAction.Next),
    )
    OutlinedTextField(
        value = token,
        onValueChange = { token = it.trim() },
        label = { Text("Pairing token") },
        modifier = Modifier.fillMaxWidth(),
        shape = MaterialTheme.shapes.medium,
        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
    )
    formError?.let {
        Text(it, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.error)
    }
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
        shape = MaterialTheme.shapes.medium,
    ) {
        Text("Connect")
    }
}
