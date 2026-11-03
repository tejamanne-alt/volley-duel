// Duel Core BLE bridge for Android.
//
// Host: advertiser + GATT server (peripheral). Guest: scanner + GATT client (central) with high
// connection priority and a larger MTU. All work runs on one HandlerThread; events are encoded as
// bytes (layout: BleEventCodec in BleProtocol.cs) into a lock-free ConcurrentLinkedQueue that Unity
// drains once per frame with poll().
//
// Android cannot set a custom local name without renaming the phone, so the court code travels as
// service data in the scan response. Phones without multiple-advertisement support can only join.
//
// Status: written against the Android BLE documentation, not yet run on hardware (P0 radio spike).

package com.duel.core.ble

import android.Manifest
import android.annotation.SuppressLint
import android.app.Activity
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothGatt
import android.bluetooth.BluetoothGattCallback
import android.bluetooth.BluetoothGattCharacteristic
import android.bluetooth.BluetoothGattDescriptor
import android.bluetooth.BluetoothGattServer
import android.bluetooth.BluetoothGattServerCallback
import android.bluetooth.BluetoothGattService
import android.bluetooth.BluetoothManager
import android.bluetooth.BluetoothProfile
import android.bluetooth.BluetoothStatusCodes
import android.bluetooth.le.AdvertiseCallback
import android.bluetooth.le.AdvertiseData
import android.bluetooth.le.AdvertiseSettings
import android.bluetooth.le.ScanCallback
import android.bluetooth.le.ScanFilter
import android.bluetooth.le.ScanResult
import android.bluetooth.le.ScanSettings
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import android.os.Handler
import android.os.HandlerThread
import android.os.ParcelUuid
import java.io.ByteArrayOutputStream
import java.util.UUID
import java.util.concurrent.ConcurrentLinkedQueue

// Permissions are checked in ready() before every radio call; the pre-Android-13 GATT calls are used
// only behind SDK_INT checks.
@SuppressLint("MissingPermission")
@Suppress("DEPRECATION")
class DuelBle(activity: Activity) {
    companion object {
        val SERVICE: UUID = UUID.fromString("6A1D0000-5C3E-4F7B-9B1E-2D8C4F0A7E31")
        val INPUT: UUID = UUID.fromString("6A1D0001-5C3E-4F7B-9B1E-2D8C4F0A7E31")
        val CONTROL: UUID = UUID.fromString("6A1D0002-5C3E-4F7B-9B1E-2D8C4F0A7E31")
        val CCCD: UUID = UUID.fromString("00002902-0000-1000-8000-00805f9b34fb")

        // Mirrors BleEventKind.
        const val ADAPTER = 1
        const val PEER_FOUND = 2
        const val CONNECTED = 3
        const val DISCONNECTED = 4
        const val RECEIVED = 5
        const val MTU_CHANGED = 6
        const val ERROR = 8
        const val ADVERTISING = 9

        const val WANT_MTU = 185
        const val DEFAULT_PAYLOAD = 20
    }

    private val context: Context = activity.applicationContext
    private val manager = context.getSystemService(Context.BLUETOOTH_SERVICE) as BluetoothManager
    private val adapter: BluetoothAdapter? = manager.adapter
    private val events = ConcurrentLinkedQueue<ByteArray>()
    private val thread = HandlerThread("duel-ble").apply { start() }
    private val worker = Handler(thread.looper)

    // Host (peripheral) state; touched only on the worker thread.
    private var server: BluetoothGattServer? = null
    private var serviceReady = false
    private var advertising = false
    private var advertName = ""
    private var advertData = ByteArray(0)
    private var guest: BluetoothDevice? = null
    private val guestSubscriptions = HashSet<UUID>()
    private var hostReady = false
    private var hostPayload = DEFAULT_PAYLOAD
    private var notifyBusy = false
    private var pendingNotifyInput: ByteArray? = null
    private val pendingIndications = ArrayDeque<ByteArray>()

    // Guest (central) state; touched only on the worker thread.
    private var scanning = false
    private val seen = HashMap<String, BluetoothDevice>()
    private val lastAdvert = HashMap<String, String>()
    private var gatt: BluetoothGatt? = null
    private var remoteInput: BluetoothGattCharacteristic? = null
    private var remoteControl: BluetoothGattCharacteristic? = null
    private var guestReady = false
    private var discoveryStarted = false
    private var guestPayload = DEFAULT_PAYLOAD
    private var opBusy = false
    private var pendingWriteInput: ByteArray? = null
    private val pendingWrites = ArrayDeque<ByteArray>()
    private val pendingSubscriptions = ArrayDeque<Pair<BluetoothGattDescriptor, ByteArray>>()

    // ------------------------------------------------------------------ Unity-facing API

    fun canAdvertise(): Boolean {
        val a = adapter ?: return false
        return a.isEnabled && a.isMultipleAdvertisementSupported && a.bluetoothLeAdvertiser != null
    }

    fun hasPermissions(): Boolean {
        val needed = if (Build.VERSION.SDK_INT >= 31) {
            arrayOf(Manifest.permission.BLUETOOTH_SCAN, Manifest.permission.BLUETOOTH_ADVERTISE, Manifest.permission.BLUETOOTH_CONNECT)
        } else {
            arrayOf(Manifest.permission.ACCESS_FINE_LOCATION)
        }
        return needed.all { context.checkSelfPermission(it) == PackageManager.PERMISSION_GRANTED }
    }

    // Every entry point returns Unit: Unity's AndroidJavaObject.Call binds to void methods.

    fun startAdvertising(localName: String, serviceData: ByteArray) {
        val data = serviceData.copyOf()
        worker.post {
            if (!ready()) return@post
            advertName = localName
            advertData = data
            openServer()
            if (serviceReady) beginAdvertising()
        }
    }

    fun stopAdvertising() {
        worker.post { endAdvertising() }
    }

    fun startScan() {
        worker.post {
            if (!ready() || scanning) return@post
            val scanner = adapter?.bluetoothLeScanner
            if (scanner == null) {
                emit(ERROR, value = 1, name = "scanner unavailable")
                return@post
            }
            val filters = listOf(ScanFilter.Builder().setServiceUuid(ParcelUuid(SERVICE)).build())
            val settings = ScanSettings.Builder().setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY).build()
            lastAdvert.clear()
            scanner.startScan(filters, settings, scanCallback)
            scanning = true
        }
    }

    fun stopScan() {
        worker.post { endScan() }
    }

    fun connect(peerId: String) {
        worker.post {
            if (!ready()) return@post
            endScan()
            val device = seen[peerId] ?: runCatching { adapter?.getRemoteDevice(peerId) }.getOrNull()
            if (device == null) {
                emit(DISCONNECTED, value = -1, peerId = peerId, name = "court not found")
                return@post
            }
            resetGuest()
            gatt = if (Build.VERSION.SDK_INT >= 26) {
                device.connectGatt(context, false, gattCallback, BluetoothDevice.TRANSPORT_LE, BluetoothDevice.PHY_LE_1M_MASK, worker)
            } else {
                device.connectGatt(context, false, gattCallback, BluetoothDevice.TRANSPORT_LE)
            }
        }
    }

    fun disconnect() {
        worker.post {
            gatt?.let {
                it.disconnect()
                it.close()
            }
            gatt = null
            resetGuest()
            endScan()
            endAdvertising()
            guest?.let { server?.cancelConnection(it) }
            resetHost()
            server?.close()
            server = null
            serviceReady = false
        }
    }

    fun send(channel: Int, data: ByteArray) {
        val copy = data.copyOf()
        worker.post {
            if (hostReady) hostSend(channel, copy) else if (guestReady) guestSend(channel, copy)
        }
    }

    /** Drains queued events as u16-length-prefixed records, or null when there are none. */
    fun poll(): ByteArray? {
        if (events.isEmpty()) return null
        val out = ByteArrayOutputStream()
        while (true) {
            val e = events.poll() ?: break
            out.write(e.size and 0xFF)
            out.write((e.size shr 8) and 0xFF)
            out.write(e)
        }
        return out.toByteArray()
    }

    // ------------------------------------------------------------------ events

    private fun emit(kind: Int, channel: Int = 0, value: Int = 0, value2: Int = 0, peerId: String = "", name: String = "", data: ByteArray = ByteArray(0)) {
        val id = peerId.toByteArray(Charsets.UTF_8)
        val text = name.toByteArray(Charsets.UTF_8)
        val out = ByteArrayOutputStream(16 + id.size + text.size + data.size)
        out.write(kind)
        out.write(channel)
        writeInt(out, value)
        writeInt(out, value2)
        writeBlock(out, id)
        writeBlock(out, text)
        writeBlock(out, data)
        events.add(out.toByteArray())
    }

    private fun writeInt(out: ByteArrayOutputStream, v: Int) {
        out.write(v and 0xFF)
        out.write((v shr 8) and 0xFF)
        out.write((v shr 16) and 0xFF)
        out.write((v shr 24) and 0xFF)
    }

    private fun writeBlock(out: ByteArrayOutputStream, block: ByteArray) {
        val n = minOf(block.size, 0xFFFF)
        out.write(n and 0xFF)
        out.write((n shr 8) and 0xFF)
        out.write(block, 0, n)
    }

    private fun ready(): Boolean {
        val a = adapter
        when {
            a == null -> emit(ADAPTER, value = -2)
            !hasPermissions() -> emit(ADAPTER, value = -1)
            !a.isEnabled -> emit(ADAPTER, value = 0)
            else -> return true
        }
        return false
    }

    // ------------------------------------------------------------------ host (peripheral)

    private fun openServer() {
        if (server != null) return
        val s = manager.openGattServer(context, serverCallback)
        if (s == null) {
            emit(ADVERTISING, value = -3, name = "GATT server unavailable")
            return
        }
        val input = BluetoothGattCharacteristic(
            INPUT,
            BluetoothGattCharacteristic.PROPERTY_WRITE_NO_RESPONSE or BluetoothGattCharacteristic.PROPERTY_NOTIFY,
            BluetoothGattCharacteristic.PERMISSION_WRITE
        )
        input.addDescriptor(BluetoothGattDescriptor(CCCD, BluetoothGattDescriptor.PERMISSION_READ or BluetoothGattDescriptor.PERMISSION_WRITE))
        val control = BluetoothGattCharacteristic(
            CONTROL,
            BluetoothGattCharacteristic.PROPERTY_WRITE or BluetoothGattCharacteristic.PROPERTY_INDICATE,
            BluetoothGattCharacteristic.PERMISSION_WRITE
        )
        control.addDescriptor(BluetoothGattDescriptor(CCCD, BluetoothGattDescriptor.PERMISSION_READ or BluetoothGattDescriptor.PERMISSION_WRITE))
        val service = BluetoothGattService(SERVICE, BluetoothGattService.SERVICE_TYPE_PRIMARY)
        service.addCharacteristic(input)
        service.addCharacteristic(control)
        server = s
        s.addService(service) // Advertising starts in onServiceAdded.
    }

    private fun beginAdvertising() {
        if (advertising) return
        val advertiser = adapter?.bluetoothLeAdvertiser
        if (advertiser == null) {
            emit(ADVERTISING, value = -2, name = "this phone cannot advertise; join a court instead")
            return
        }
        val settings = AdvertiseSettings.Builder()
            .setAdvertiseMode(AdvertiseSettings.ADVERTISE_MODE_LOW_LATENCY)
            .setTxPowerLevel(AdvertiseSettings.ADVERTISE_TX_POWER_HIGH)
            .setConnectable(true)
            .build()
        val data = AdvertiseData.Builder()
            .addServiceUuid(ParcelUuid(SERVICE))
            .setIncludeDeviceName(false)
            .build()
        val scanResponse = AdvertiseData.Builder()
            .addServiceData(ParcelUuid(SERVICE), advertData)
            .setIncludeDeviceName(false)
            .build()
        advertiser.startAdvertising(settings, data, scanResponse, advertiseCallback)
        advertising = true
    }

    private fun endAdvertising() {
        if (!advertising) return
        advertising = false
        adapter?.bluetoothLeAdvertiser?.stopAdvertising(advertiseCallback)
    }

    private val advertiseCallback = object : AdvertiseCallback() {
        override fun onStartSuccess(settingsInEffect: AdvertiseSettings) {
            worker.post { emit(ADVERTISING, value = 1) }
        }

        override fun onStartFailure(errorCode: Int) {
            worker.post {
                advertising = false
                emit(ADVERTISING, value = -10 - errorCode, name = "advertising failed ($errorCode)")
            }
        }
    }

    private fun resetHost() {
        guest = null
        guestSubscriptions.clear()
        hostReady = false
        hostPayload = DEFAULT_PAYLOAD
        notifyBusy = false
        pendingNotifyInput = null
        pendingIndications.clear()
    }

    private val serverCallback = object : BluetoothGattServerCallback() {
        override fun onServiceAdded(status: Int, service: BluetoothGattService) {
            worker.post {
                if (status == BluetoothGatt.GATT_SUCCESS) {
                    serviceReady = true
                    if (advertName.isNotEmpty() || advertData.isNotEmpty()) beginAdvertising()
                } else {
                    emit(ADVERTISING, value = -4, name = "could not add the court service ($status)")
                }
            }
        }

        override fun onConnectionStateChange(device: BluetoothDevice, status: Int, newState: Int) {
            worker.post {
                if (newState == BluetoothProfile.STATE_DISCONNECTED && device.address == guest?.address) {
                    val wasReady = hostReady
                    resetHost()
                    if (wasReady) emit(DISCONNECTED, value = status, peerId = device.address)
                }
            }
        }

        override fun onMtuChanged(device: BluetoothDevice, mtu: Int) {
            worker.post {
                hostPayload = maxOf(DEFAULT_PAYLOAD, mtu - 3)
                if (hostReady) emit(MTU_CHANGED, value = hostPayload, value2 = hostPayload)
            }
        }

        override fun onDescriptorWriteRequest(
            device: BluetoothDevice, requestId: Int, descriptor: BluetoothGattDescriptor,
            preparedWrite: Boolean, responseNeeded: Boolean, offset: Int, value: ByteArray?
        ) {
            if (responseNeeded) server?.sendResponse(device, requestId, BluetoothGatt.GATT_SUCCESS, offset, value)
            val bytes = value?.copyOf()
            worker.post {
                if (descriptor.uuid != CCCD) return@post
                val current = guest
                if (current != null && current.address != device.address) return@post // One guest per court.
                guest = device
                val on = bytes != null && bytes.isNotEmpty() && (bytes[0].toInt() and 0x03) != 0
                val characteristic = descriptor.characteristic.uuid
                if (on) guestSubscriptions.add(characteristic) else guestSubscriptions.remove(characteristic)
                if (!hostReady && guestSubscriptions.contains(INPUT) && guestSubscriptions.contains(CONTROL)) {
                    hostReady = true
                    endAdvertising()
                    emit(CONNECTED, value = hostPayload, value2 = hostPayload, peerId = device.address)
                }
            }
        }

        override fun onCharacteristicWriteRequest(
            device: BluetoothDevice, requestId: Int, characteristic: BluetoothGattCharacteristic,
            preparedWrite: Boolean, responseNeeded: Boolean, offset: Int, value: ByteArray?
        ) {
            if (responseNeeded) server?.sendResponse(device, requestId, BluetoothGatt.GATT_SUCCESS, offset, null)
            val bytes = value?.copyOf() ?: return
            val channel = if (characteristic.uuid == INPUT) 0 else 1
            worker.post {
                if (hostReady && device.address == guest?.address && !preparedWrite) emit(RECEIVED, channel = channel, data = bytes)
            }
        }

        override fun onNotificationSent(device: BluetoothDevice, status: Int) {
            worker.post {
                notifyBusy = false
                pumpNotifications()
            }
        }
    }

    private fun hostSend(channel: Int, data: ByteArray) {
        if (channel == 0) pendingNotifyInput = data else pendingIndications.addLast(data)
        pumpNotifications()
    }

    /** One notification or indication in flight at a time; the newest input packet goes first. */
    private fun pumpNotifications() {
        if (notifyBusy) return
        val device = guest ?: return
        val s = server ?: return
        val service = s.getService(SERVICE) ?: return
        val isInput: Boolean
        val value: ByteArray
        val input = pendingNotifyInput
        if (input != null) {
            isInput = true
            value = input
            pendingNotifyInput = null
        } else {
            value = pendingIndications.removeFirstOrNull() ?: return
            isInput = false
        }
        val characteristic = service.getCharacteristic(if (isInput) INPUT else CONTROL) ?: return
        val ok = if (Build.VERSION.SDK_INT >= 33) {
            s.notifyCharacteristicChanged(device, characteristic, !isInput, value) == BluetoothStatusCodes.SUCCESS
        } else {
            characteristic.value = value
            s.notifyCharacteristicChanged(device, characteristic, !isInput)
        }
        if (ok) {
            notifyBusy = true
        } else {
            if (isInput) {
                if (pendingNotifyInput == null) pendingNotifyInput = value
            } else {
                pendingIndications.addFirst(value)
            }
            worker.postDelayed({ pumpNotifications() }, 4)
        }
    }

    // ------------------------------------------------------------------ guest (central)

    private fun endScan() {
        if (!scanning) return
        scanning = false
        adapter?.bluetoothLeScanner?.stopScan(scanCallback)
    }

    private val scanCallback = object : ScanCallback() {
        override fun onScanResult(callbackType: Int, result: ScanResult) {
            worker.post {
                val record = result.scanRecord
                val name = record?.deviceName ?: ""
                val data = record?.getServiceData(ParcelUuid(SERVICE)) ?: ByteArray(0)
                val id = result.device.address
                seen[id] = result.device
                // Report again when the advert changes (a scan response often lands after the first result).
                val signature = name + "|" + data.joinToString(",")
                if (lastAdvert[id] == signature) return@post
                lastAdvert[id] = signature
                emit(PEER_FOUND, value = result.rssi, peerId = id, name = name, data = data)
            }
        }

        override fun onScanFailed(errorCode: Int) {
            worker.post {
                scanning = false
                emit(ERROR, value = errorCode, name = "scan failed ($errorCode)")
            }
        }
    }

    private fun resetGuest() {
        guestReady = false
        discoveryStarted = false
        remoteInput = null
        remoteControl = null
        guestPayload = DEFAULT_PAYLOAD
        opBusy = false
        pendingWriteInput = null
        pendingWrites.clear()
        pendingSubscriptions.clear()
    }

    private fun discover(g: BluetoothGatt) {
        if (discoveryStarted) return
        discoveryStarted = true
        g.discoverServices()
    }

    private val gattCallback = object : BluetoothGattCallback() {
        override fun onConnectionStateChange(g: BluetoothGatt, status: Int, newState: Int) {
            worker.post {
                if (g != gatt) return@post
                if (newState == BluetoothProfile.STATE_CONNECTED && status == BluetoothGatt.GATT_SUCCESS) {
                    g.requestConnectionPriority(BluetoothGatt.CONNECTION_PRIORITY_HIGH)
                    if (!g.requestMtu(WANT_MTU)) discover(g)
                    // Some phones never answer the MTU request: discover anyway after a moment.
                    worker.postDelayed({ if (g == gatt) discover(g) }, 1500)
                } else if (newState == BluetoothProfile.STATE_DISCONNECTED) {
                    resetGuest()
                    g.close()
                    gatt = null
                    emit(DISCONNECTED, value = status, peerId = g.device.address)
                }
            }
        }

        override fun onMtuChanged(g: BluetoothGatt, mtu: Int, status: Int) {
            worker.post {
                if (status == BluetoothGatt.GATT_SUCCESS) guestPayload = maxOf(DEFAULT_PAYLOAD, mtu - 3)
                if (guestReady) emit(MTU_CHANGED, value = guestPayload, value2 = guestPayload) else discover(g)
            }
        }

        override fun onServicesDiscovered(g: BluetoothGatt, status: Int) {
            worker.post {
                val service = g.getService(SERVICE)
                val input = service?.getCharacteristic(INPUT)
                val control = service?.getCharacteristic(CONTROL)
                if (status != BluetoothGatt.GATT_SUCCESS || input == null || control == null) {
                    emit(ERROR, value = status, name = "court service not found")
                    g.disconnect()
                    return@post
                }
                remoteInput = input
                remoteControl = control
                subscribe(g, input, BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE)
                subscribe(g, control, BluetoothGattDescriptor.ENABLE_INDICATION_VALUE)
                pumpWrites()
            }
        }

        override fun onDescriptorWrite(g: BluetoothGatt, descriptor: BluetoothGattDescriptor, status: Int) {
            worker.post {
                opBusy = false
                if (status != BluetoothGatt.GATT_SUCCESS) {
                    emit(ERROR, value = status, name = "subscription failed")
                    g.disconnect()
                    return@post
                }
                if (!guestReady && pendingSubscriptions.isEmpty()) {
                    guestReady = true
                    emit(CONNECTED, value = guestPayload, value2 = guestPayload, peerId = g.device.address)
                }
                pumpWrites()
            }
        }

        override fun onCharacteristicWrite(g: BluetoothGatt, characteristic: BluetoothGattCharacteristic, status: Int) {
            worker.post {
                opBusy = false
                if (status != BluetoothGatt.GATT_SUCCESS) emit(ERROR, value = status, name = "write failed")
                pumpWrites()
            }
        }

        // Android 13+.
        override fun onCharacteristicChanged(g: BluetoothGatt, characteristic: BluetoothGattCharacteristic, value: ByteArray) {
            val bytes = value.copyOf()
            val channel = if (characteristic.uuid == INPUT) 0 else 1
            worker.post { if (guestReady) emit(RECEIVED, channel = channel, data = bytes) }
        }

        // Android 12 and older.
        @Deprecated("Replaced on Android 13 by the overload with a value")
        override fun onCharacteristicChanged(g: BluetoothGatt, characteristic: BluetoothGattCharacteristic) {
            val bytes = characteristic.value?.copyOf() ?: return
            val channel = if (characteristic.uuid == INPUT) 0 else 1
            worker.post { if (guestReady) emit(RECEIVED, channel = channel, data = bytes) }
        }
    }

    private fun subscribe(g: BluetoothGatt, characteristic: BluetoothGattCharacteristic, value: ByteArray) {
        g.setCharacteristicNotification(characteristic, true)
        val cccd = characteristic.getDescriptor(CCCD) ?: return
        pendingSubscriptions.addLast(Pair(cccd, value))
    }

    private fun guestSend(channel: Int, data: ByteArray) {
        if (channel == 0) pendingWriteInput = data else pendingWrites.addLast(data)
        pumpWrites()
    }

    /** The GATT client allows one operation at a time: subscriptions, then the newest input, then control writes. */
    private fun pumpWrites() {
        if (opBusy) return
        val g = gatt ?: return
        val subscription = pendingSubscriptions.removeFirstOrNull()
        if (subscription != null) {
            val (descriptor, value) = subscription
            val ok = if (Build.VERSION.SDK_INT >= 33) {
                g.writeDescriptor(descriptor, value) == BluetoothStatusCodes.SUCCESS
            } else {
                descriptor.value = value
                g.writeDescriptor(descriptor)
            }
            if (ok) opBusy = true else {
                pendingSubscriptions.addFirst(subscription)
                worker.postDelayed({ pumpWrites() }, 4)
            }
            return
        }
        if (!guestReady) return
        val input = pendingWriteInput
        val characteristic: BluetoothGattCharacteristic
        val value: ByteArray
        val type: Int
        if (input != null) {
            characteristic = remoteInput ?: return
            value = input
            type = BluetoothGattCharacteristic.WRITE_TYPE_NO_RESPONSE
            pendingWriteInput = null
        } else {
            value = pendingWrites.removeFirstOrNull() ?: return
            characteristic = remoteControl ?: return
            type = BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT
        }
        val ok = if (Build.VERSION.SDK_INT >= 33) {
            g.writeCharacteristic(characteristic, value, type) == BluetoothStatusCodes.SUCCESS
        } else {
            characteristic.writeType = type
            characteristic.value = value
            g.writeCharacteristic(characteristic)
        }
        if (ok) {
            opBusy = true
        } else {
            if (type == BluetoothGattCharacteristic.WRITE_TYPE_NO_RESPONSE) {
                if (pendingWriteInput == null) pendingWriteInput = value
            } else {
                pendingWrites.addFirst(value)
            }
            worker.postDelayed({ pumpWrites() }, 4)
        }
    }
}
