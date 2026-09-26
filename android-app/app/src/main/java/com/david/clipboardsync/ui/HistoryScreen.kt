package com.david.clipboardsync.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.foundation.layout.Row
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.ui.Alignment
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.text.style.TextOverflow
import com.david.clipboardsync.FileTransfer
import com.david.clipboardsync.HistoryItem

@Composable
fun HistoryScreen(
    history: List<HistoryItem>,
    needsBatteryExemption: Boolean,
    onAllowBattery: () -> Unit,
    onCopyText: (String) -> Unit,
    onCopyImage: (ByteArray) -> Unit,
    onDelete: (Long) -> Unit,
    transfers: List<FileTransfer>,
    onCancelTransfer: (String) -> Unit,
    onOpenFile: (HistoryItem.FileItem) -> Unit,
    onOpenDownloads: () -> Unit,
    modifier: Modifier = Modifier,
) {
    LazyColumn(
        modifier = modifier.fillMaxSize(),
        // The bottom inset keeps the last card clear of the Send file button.
        contentPadding = PaddingValues(start = ScreenPadding, end = ScreenPadding, top = 16.dp, bottom = 96.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        items(transfers, key = { "transfer-${it.id}" }) { transfer ->
            TransferCard(transfer, onCancelTransfer, Modifier.animateItem())
        }
        if (needsBatteryExemption) {
            item(key = "battery") {
                Card(
                    modifier = Modifier.fillMaxWidth(),
                    shape = MaterialTheme.shapes.large,
                    colors = CardDefaults.cardColors(
                        containerColor = MaterialTheme.colorScheme.primaryContainer,
                        contentColor = MaterialTheme.colorScheme.onPrimaryContainer,
                    ),
                ) {
                    Column(
                        modifier = Modifier.padding(start = 18.dp, end = 8.dp, top = 16.dp, bottom = 4.dp),
                    ) {
                        Text("Keep ClipBoard connected", style = MaterialTheme.typography.titleMedium)
                        Text(
                            "Allow ClipBoard to run in the background without battery restrictions so you can receive items instantly.",
                            style = MaterialTheme.typography.bodyMedium,
                            modifier = Modifier.padding(top = 4.dp, end = 10.dp),
                        )
                        TextButton(onClick = onAllowBattery) { Text("Allow") }
                    }
                }
            }
        }
        if (history.isEmpty() && transfers.isEmpty()) {
            item(key = "empty") {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 12.dp, vertical = 64.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    Text(
                        "Nothing synced yet",
                        style = MaterialTheme.typography.titleMedium,
                        textAlign = TextAlign.Center,
                        modifier = Modifier.fillMaxWidth(),
                    )
                    Text(
                        "Copy something on the PC, or use the Send to PC tile in Quick Settings.",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        textAlign = TextAlign.Center,
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
            }
        }
        items(history, key = { it.id }) { item ->
            HistoryItemCard(
                item = item,
                onCopyText = onCopyText,
                onCopyImage = onCopyImage,
                onDelete = onDelete,
                onOpenFile = onOpenFile,
                onOpenDownloads = onOpenDownloads,
                modifier = Modifier.animateItem(),
            )
        }
    }
}

@Composable
private fun TransferCard(
    transfer: FileTransfer,
    onCancel: (String) -> Unit,
    modifier: Modifier = Modifier,
) {
    SectionCard(modifier) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    transfer.fileName,
                    style = MaterialTheme.typography.titleMedium,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    "${if (transfer.sending) "Sending" else "Receiving"} · " +
                        "${formatBytes(transfer.bytesDone)} of ${formatBytes(transfer.totalBytes)}",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            if (transfer.sending) {
                TextButton(onClick = { onCancel(transfer.id) }) { Text("Cancel") }
            }
        }
        LinearProgressIndicator(
            progress = { transfer.fraction },
            modifier = Modifier.fillMaxWidth(),
            strokeCap = StrokeCap.Round,
        )
    }
}
