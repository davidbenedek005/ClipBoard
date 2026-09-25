# Release builds keep R8 off (isMinifyEnabled = false in app/build.gradle.kts).
# Nothing is stripped, so OkHttp, org.json, and EncryptedSharedPreferences do not
# need keep rules for the sideload APK. This file is the rules path if minify is
# turned on later. The app does not use kotlinx.serialization.

-keepattributes Signature, InnerClasses, EnclosingMethod
-keepattributes RuntimeVisibleAnnotations, RuntimeVisibleParameterAnnotations
-keepattributes AnnotationDefault

-dontwarn okhttp3.**
-dontwarn okio.**
-keep class okhttp3.** { *; }
-keep interface okhttp3.** { *; }
-keep class okio.** { *; }

-keep class com.david.clipboardsync.crypto.** { *; }
-keep class androidx.security.crypto.** { *; }
