package com.david.clipboardsync.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
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

@Composable
fun ManualConnectionScreen(
    onConnect: (ip: String, port: Int, token: String) -> Unit,
    onClose: () -> Unit,
) {
    var ip by rememberSaveable { mutableStateOf("") }
    var port by rememberSaveable { mutableStateOf("53211") }
    var token by rememberSaveable { mutableStateOf("") }
    var formError by rememberSaveable { mutableStateOf<String?>(null) }
    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Text("Manual connection", style = MaterialTheme.typography.headlineMedium)
        Text(
            "Copy the IP, port, and token from the PC pairing window. " +
                "Scanning the QR with the phone's camera still pairs on its own.",
            style = MaterialTheme.typography.bodyMedium,
        )
        OutlinedTextField(
            value = ip,
            onValueChange = { ip = it },
            label = { Text("IP address") },
            modifier = Modifier.fillMaxWidth(),
            singleLine = true,
        )
        OutlinedTextField(
            value = port,
            onValueChange = { port = it.filter { ch -> ch.isDigit() }.take(5) },
            label = { Text("Port") },
            modifier = Modifier.fillMaxWidth(),
            singleLine = true,
        )
        OutlinedTextField(
            value = token,
            onValueChange = { token = it.trim() },
            label = { Text("Pairing token") },
            modifier = Modifier.fillMaxWidth(),
        )
        formError?.let { Text(it) }
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
        ) {
            Text("Connect")
        }
        OutlinedButton(onClick = onClose, modifier = Modifier.fillMaxWidth()) {
            Text("Cancel")
        }
    }
}
