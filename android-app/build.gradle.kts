plugins {
    // AGP 8.7.3 is the newest stable line that still targets compileSdk 35.
    // Compose 1.12 / AGP 9 require compileSdk 37, which this project does not use.
    id("com.android.application") version "8.7.3" apply false
    id("org.jetbrains.kotlin.android") version "2.0.21" apply false
    id("org.jetbrains.kotlin.plugin.compose") version "2.0.21" apply false
}
