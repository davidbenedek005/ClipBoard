package com.david.clipboardsync

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import java.io.ByteArrayOutputStream
import kotlin.math.max

/**
 * JPEG for the wire. Plaintext is the UTF-8 of standard Base64 of these bytes,
 * then AES-GCM, matching protocol-schema.json.
 */
object ImageCodec {
    const val MAX_EDGE = 1600
    const val MAX_JPEG_BYTES = 5 * 1024 * 1024

    fun toJpeg(encoded: ByteArray): ByteArray? {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(encoded, 0, encoded.size, bounds)
        if (bounds.outWidth <= 0 || bounds.outHeight <= 0) {
            return null
        }
        val sample = sampleSize(bounds.outWidth, bounds.outHeight)
        val bitmap = BitmapFactory.decodeByteArray(
            encoded,
            0,
            encoded.size,
            BitmapFactory.Options().apply { inSampleSize = sample },
        ) ?: return null
        val scaled = scale(bitmap)
        if (scaled !== bitmap) {
            bitmap.recycle()
        }
        val jpeg = compress(scaled)
        scaled.recycle()
        return jpeg
    }

    private fun sampleSize(width: Int, height: Int): Int {
        var sample = 1
        while (width / sample > MAX_EDGE * 2 || height / sample > MAX_EDGE * 2) {
            sample *= 2
        }
        return sample
    }

    private fun scale(bitmap: Bitmap): Bitmap {
        val longest = max(bitmap.width, bitmap.height)
        if (longest <= MAX_EDGE) {
            return bitmap
        }
        val ratio = MAX_EDGE.toFloat() / longest
        val width = (bitmap.width * ratio).toInt().coerceAtLeast(1)
        val height = (bitmap.height * ratio).toInt().coerceAtLeast(1)
        return Bitmap.createScaledBitmap(bitmap, width, height, true)
    }

    private fun compress(bitmap: Bitmap): ByteArray? {
        var quality = 85
        while (quality >= 40) {
            val out = ByteArrayOutputStream()
            bitmap.compress(Bitmap.CompressFormat.JPEG, quality, out)
            if (out.size() <= MAX_JPEG_BYTES) {
                return out.toByteArray()
            }
            quality -= 15
        }
        return null
    }
}
