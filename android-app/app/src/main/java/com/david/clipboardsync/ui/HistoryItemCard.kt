package com.david.clipboardsync.ui

import android.graphics.BitmapFactory
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.unit.dp
import com.david.clipboardsync.HistoryItem

@Composable
fun HistoryItemCard(
    item: HistoryItem,
    onCopyText: (String) -> Unit,
    onCopyImage: (ByteArray) -> Unit,
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(20.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainerLow),
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    "${item.time}  ${item.direction}",
                    style = MaterialTheme.typography.labelLarge,
                    color = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.weight(1f),
                )
                when (item) {
                    is HistoryItem.TextItem -> IconButton(onClick = { onCopyText(item.text) }) {
                        Text("Copy", style = MaterialTheme.typography.labelLarge)
                    }
                    is HistoryItem.ImageItem -> IconButton(onClick = { onCopyImage(item.jpeg) }) {
                        Text("Copy", style = MaterialTheme.typography.labelLarge)
                    }
                }
            }
            when (item) {
                is HistoryItem.TextItem -> Text(item.text, style = MaterialTheme.typography.bodyLarge)
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
                                .heightIn(max = 220.dp)
                                .clip(RoundedCornerShape(16.dp)),
                            contentScale = ContentScale.Crop,
                        )
                    }
                }
            }
        }
    }
}
