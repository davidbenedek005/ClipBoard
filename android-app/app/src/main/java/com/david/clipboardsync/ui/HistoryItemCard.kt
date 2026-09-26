package com.david.clipboardsync.ui

import android.graphics.BitmapFactory
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ElevatedCard
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.david.clipboardsync.HistoryItem

@Composable
fun HistoryItemCard(
    item: HistoryItem,
    onCopyText: (String) -> Unit,
    onCopyImage: (ByteArray) -> Unit,
    onDelete: (Long) -> Unit,
    onOpenFile: (HistoryItem.FileItem) -> Unit,
    onOpenDownloads: () -> Unit,
    modifier: Modifier = Modifier,
) {
    ElevatedCard(
        modifier = modifier.fillMaxWidth(),
        shape = MaterialTheme.shapes.large,
        colors = CardDefaults.elevatedCardColors(containerColor = MaterialTheme.colorScheme.surfaceContainerLow),
        elevation = CardDefaults.elevatedCardElevation(defaultElevation = 1.dp),
    ) {
        Column(
            modifier = Modifier.padding(horizontal = 18.dp, vertical = 16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Surface(
                    shape = RoundedCornerShape(8.dp),
                    color = MaterialTheme.colorScheme.primaryContainer,
                ) {
                    Text(
                        item.direction,
                        style = MaterialTheme.typography.labelMedium,
                        color = MaterialTheme.colorScheme.onPrimaryContainer,
                        modifier = Modifier.padding(horizontal = 10.dp, vertical = 4.dp),
                    )
                }
                Spacer(Modifier.width(10.dp))
                Text(
                    item.time,
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.weight(1f),
                )
                when (item) {
                    is HistoryItem.TextItem -> FilledTonalButton(
                        onClick = { onCopyText(item.text) },
                        shape = MaterialTheme.shapes.small,
                    ) { Text("Copy") }
                    is HistoryItem.ImageItem -> FilledTonalButton(
                        onClick = { onCopyImage(item.jpeg) },
                        shape = MaterialTheme.shapes.small,
                    ) { Text("Copy") }
                    is HistoryItem.FileItem -> Unit
                }
                IconButton(onClick = { onDelete(item.id) }) {
                    Icon(
                        Icons.Outlined.Delete,
                        contentDescription = "Delete",
                        tint = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }
            when (item) {
                is HistoryItem.TextItem -> Text(
                    item.text,
                    style = MaterialTheme.typography.bodyLarge,
                    color = MaterialTheme.colorScheme.onSurface,
                    maxLines = 8,
                    overflow = TextOverflow.Ellipsis,
                )
                is HistoryItem.ImageItem -> {
                    val bitmap = remember(item.jpeg) {
                        BitmapFactory.decodeByteArray(item.jpeg, 0, item.jpeg.size)?.asImageBitmap()
                    }
                    if (bitmap != null) {
                        Image(
                            bitmap = bitmap,
                            contentDescription = "Synced image",
                            modifier = Modifier
                                .fillMaxWidth()
                                .heightIn(max = 240.dp)
                                .clip(MaterialTheme.shapes.medium),
                            contentScale = ContentScale.Crop,
                        )
                    } else {
                        Text(
                            "Image could not be shown.",
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
                is HistoryItem.FileItem -> FileBody(item, onOpenFile, onOpenDownloads)
            }
        }
    }
}

@Composable
private fun FileBody(
    item: HistoryItem.FileItem,
    onOpenFile: (HistoryItem.FileItem) -> Unit,
    onOpenDownloads: () -> Unit,
) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        FileBadge(item.fileName)
        Spacer(Modifier.width(14.dp))
        Column(modifier = Modifier.weight(1f)) {
            Text(
                item.fileName,
                style = MaterialTheme.typography.titleMedium,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                formatBytes(item.size),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
    if (item.uri != null) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            FilledTonalButton(onClick = { onOpenFile(item) }, shape = MaterialTheme.shapes.small) {
                Text("Open file")
            }
            OutlinedButton(onClick = onOpenDownloads, shape = MaterialTheme.shapes.small) {
                Text("Downloads")
            }
        }
    }
}

@Composable
private fun FileBadge(fileName: String) {
    val extension = fileName.substringAfterLast('.', "").uppercase().takeIf { it.length in 1..4 } ?: "FILE"
    Surface(
        modifier = Modifier.size(width = 44.dp, height = 52.dp),
        shape = MaterialTheme.shapes.small,
        color = MaterialTheme.colorScheme.primaryContainer,
    ) {
        Box(contentAlignment = Alignment.Center) {
            Text(
                extension,
                style = MaterialTheme.typography.labelMedium,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.onPrimaryContainer,
                maxLines = 1,
            )
        }
    }
}
