package com.david.clipboardsync.network

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.util.Log
import com.david.clipboardsync.SyncHistory

/**
 * Resolves `_clipboardsync._tcp` advertised by the Windows app. When the TXT
 * `id` matches the paired PC, the stored host is updated so a DHCP change
 * does not require a new QR scan.
 */
class DiscoveryManager(
    context: Context,
    private val pairing: PairingManager,
    private val onHost: (String, Int) -> Unit,
) {
    private val nsd = context.applicationContext.getSystemService(NsdManager::class.java)
    private var discovering = false

    fun start() {
        if (discovering || pairing.load() == null) {
            return
        }
        discovering = true
        try {
            nsd.discoverServices(SERVICE_TYPE, NsdManager.PROTOCOL_DNS_SD, discoveryListener)
        } catch (ex: Exception) {
            discovering = false
            Log.w(TAG, "mDNS discover failed: ${ex.message}")
        }
    }

    fun stop() {
        if (!discovering) {
            return
        }
        discovering = false
        try {
            nsd.stopServiceDiscovery(discoveryListener)
        } catch (_: Exception) {
        }
    }

    private val discoveryListener = object : NsdManager.DiscoveryListener {
        override fun onDiscoveryStarted(serviceType: String) {}

        override fun onServiceFound(service: NsdServiceInfo) {
            if (service.serviceType?.contains("clipboardsync") != true) {
                return
            }
            try {
                nsd.resolveService(service, resolveListener)
            } catch (ex: Exception) {
                Log.w(TAG, "mDNS resolve failed: ${ex.message}")
            }
        }

        override fun onServiceLost(service: NsdServiceInfo) {}

        override fun onDiscoveryStopped(serviceType: String) {
            discovering = false
        }

        override fun onStartDiscoveryFailed(serviceType: String, errorCode: Int) {
            discovering = false
            Log.w(TAG, "mDNS start failed: $errorCode")
        }

        override fun onStopDiscoveryFailed(serviceType: String, errorCode: Int) {}
    }

    private val resolveListener = object : NsdManager.ResolveListener {
        override fun onResolveFailed(serviceInfo: NsdServiceInfo, errorCode: Int) {
            Log.w(TAG, "mDNS resolve error $errorCode")
        }

        override fun onServiceResolved(serviceInfo: NsdServiceInfo) {
            val record = pairing.load() ?: return
            val advertisedId = serviceInfo.attributes?.get("id")?.toString(Charsets.UTF_8)
            val expected = record.pcDeviceId
            if (!expected.isNullOrBlank() && advertisedId != expected) {
                return
            }
            val host = serviceInfo.host?.hostAddress ?: return
            if (host == record.ip && serviceInfo.port == record.port) {
                return
            }
            pairing.updateHost(host, serviceInfo.port)
            SyncHistory.addText("Network", "PC address updated to $host")
            onHost(host, serviceInfo.port)
        }
    }

    companion object {
        private const val TAG = "ClipBoardSync"
        const val SERVICE_TYPE = "_clipboardsync._tcp."
    }
}
