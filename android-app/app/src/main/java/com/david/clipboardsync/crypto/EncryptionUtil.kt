package com.david.clipboardsync.crypto

import android.util.Base64
import java.security.MessageDigest
import javax.crypto.Cipher
import javax.crypto.Mac
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * Must match Windows [EncryptionService]: HKDF-SHA256 salt `clipboardsync-v1`,
 * info `clipboard-payload`, 32-byte key, 12-byte nonce, 16-byte GCM tag stored
 * separately from the ciphertext.
 */
object EncryptionUtil {
    private const val SALT = "clipboardsync-v1"
    private const val INFO = "clipboard-payload"

    fun deriveKey(pairingToken: String): ByteArray {
        val prk = hmac(
            SALT.toByteArray(Charsets.UTF_8),
            pairingToken.toByteArray(Charsets.UTF_8),
        )
        val info = INFO.toByteArray(Charsets.UTF_8) + byteArrayOf(1)
        return hmac(prk, info).copyOf(32)
    }

    fun encrypt(key: ByteArray, plaintext: ByteArray): Encrypted {
        val iv = ByteArray(12).also { java.security.SecureRandom().nextBytes(it) }
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(128, iv))
        val combined = cipher.doFinal(plaintext)
        val tagLength = 16
        return Encrypted(
            ciphertext = combined.copyOf(combined.size - tagLength),
            iv = iv,
            tag = combined.copyOfRange(combined.size - tagLength, combined.size),
        )
    }

    fun decrypt(key: ByteArray, iv: ByteArray, ciphertext: ByteArray, tag: ByteArray): ByteArray {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(128, iv))
        return cipher.doFinal(ciphertext + tag)
    }

    /**
     * One file chunk as a binary frame: header | nonce | ciphertext | tag. The header
     * (transfer id + index) is the GCM associated data. Same layout as Windows FileChunkCodec.
     */
    fun sealChunk(key: ByteArray, header: ByteArray, plain: ByteArray, length: Int): ByteArray {
        val nonce = ByteArray(NONCE_SIZE).also { random.nextBytes(it) }
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(128, nonce))
        cipher.updateAAD(header)
        val frame = ByteArray(header.size + NONCE_SIZE + length + TAG_SIZE)
        header.copyInto(frame)
        nonce.copyInto(frame, header.size)
        cipher.doFinal(plain, 0, length, frame, header.size + NONCE_SIZE)
        return frame
    }

    /** Returns the chunk plaintext; throws [javax.crypto.AEADBadTagException] if the frame was altered. */
    fun openChunk(key: ByteArray, frame: ByteArray, headerSize: Int): ByteArray {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(128, frame, headerSize, NONCE_SIZE))
        cipher.updateAAD(frame, 0, headerSize)
        val start = headerSize + NONCE_SIZE
        return cipher.doFinal(frame, start, frame.size - start)
    }

    private const val NONCE_SIZE = 12
    private const val TAG_SIZE = 16
    private val random = java.security.SecureRandom()

    fun sha256Hex(data: ByteArray): String {
        val digest = MessageDigest.getInstance("SHA-256").digest(data)
        return digest.joinToString("") { "%02x".format(it) }
    }

    fun b64(data: ByteArray): String = Base64.encodeToString(data, Base64.NO_WRAP)

    fun b64Decode(text: String): ByteArray = Base64.decode(text, Base64.NO_WRAP)

    private fun hmac(key: ByteArray, data: ByteArray): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(key, "HmacSHA256"))
        return mac.doFinal(data)
    }

    data class Encrypted(val ciphertext: ByteArray, val iv: ByteArray, val tag: ByteArray)
}
