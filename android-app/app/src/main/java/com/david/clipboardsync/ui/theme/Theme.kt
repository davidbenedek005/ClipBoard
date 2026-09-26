package com.david.clipboardsync.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp

val Blue = Color(0xFF005FB8)
val ConnectedGreen = Color(0xFF10893E)

private val LightColors = lightColorScheme(
    primary = Blue,
    onPrimary = Color.White,
    primaryContainer = Color(0xFFD6E6FA),
    onPrimaryContainer = Color(0xFF001B3D),
    secondary = Color(0xFF4F6078),
    onSecondary = Color.White,
    secondaryContainer = Color(0xFFE3EAF4),
    onSecondaryContainer = Color(0xFF0B1D33),
    background = Color(0xFFF5F7FA),
    onBackground = Color(0xFF1A1C20),
    surface = Color(0xFFF5F7FA),
    onSurface = Color(0xFF1A1C20),
    onSurfaceVariant = Color(0xFF5B6472),
    surfaceContainerLowest = Color.White,
    surfaceContainerLow = Color.White,
    surfaceContainer = Color(0xFFEEF1F5),
    surfaceContainerHigh = Color(0xFFE8ECF1),
    surfaceContainerHighest = Color(0xFFE2E6EC),
    outline = Color(0xFF8A93A0),
    outlineVariant = Color(0xFFDDE2E9),
)

private val DarkColors = darkColorScheme(
    primary = Color(0xFFA8C8FF),
    onPrimary = Color(0xFF00305F),
    primaryContainer = Color(0xFF004786),
    onPrimaryContainer = Color(0xFFD6E6FA),
    secondary = Color(0xFFB8C7DE),
    onSecondary = Color(0xFF223246),
    secondaryContainer = Color(0xFF2C3A4E),
    onSecondaryContainer = Color(0xFFD7E3F7),
    background = Color(0xFF111317),
    onBackground = Color(0xFFE2E4E9),
    surface = Color(0xFF111317),
    onSurface = Color(0xFFE2E4E9),
    onSurfaceVariant = Color(0xFFA9B1BD),
    surfaceContainerLowest = Color(0xFF0C0E11),
    surfaceContainerLow = Color(0xFF1A1D22),
    surfaceContainer = Color(0xFF1E2126),
    surfaceContainerHigh = Color(0xFF262A30),
    surfaceContainerHighest = Color(0xFF30343B),
    outline = Color(0xFF7D8591),
    outlineVariant = Color(0xFF3A3F47),
)

private val AppShapes = Shapes(
    extraSmall = RoundedCornerShape(8.dp),
    small = RoundedCornerShape(12.dp),
    medium = RoundedCornerShape(16.dp),
    large = RoundedCornerShape(20.dp),
    extraLarge = RoundedCornerShape(28.dp),
)

@Composable
fun ClipBoardTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit,
) {
    MaterialTheme(
        colorScheme = if (darkTheme) DarkColors else LightColors,
        shapes = AppShapes,
        content = content,
    )
}
