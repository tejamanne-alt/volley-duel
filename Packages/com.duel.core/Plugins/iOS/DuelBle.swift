// Duel Core BLE bridge for iOS (CoreBluetooth).
//
// The host runs as a peripheral: a GATT server advertising the court service, with the court code in
// the local name. The guest runs as a central: it scans by service UUID, connects and subscribes to
// both characteristics. Every CoreBluetooth callback runs on one serial queue. Events go to C# through
// a C callback as bytes (layout: BleEventCodec in BleProtocol.cs), and C# only copies them into a
// lock-free queue that the main thread drains.
//
// Spec notes: the host requests low connection latency; INPUT honors write-without-response flow
// control (newest packet only); NSBluetoothAlwaysUsageDescription is added by the build postprocess.
//
// Status: written against CoreBluetooth documentation, not yet run on hardware (P0 radio spike).

import CoreBluetooth
import Foundation

public typealias DuelBleEventCallback = @convention(c) (UnsafePointer<UInt8>?, Int32) -> Void

private let serviceUUID = CBUUID(string: "6A1D0000-5C3E-4F7B-9B1E-2D8C4F0A7E31")
private let inputUUID = CBUUID(string: "6A1D0001-5C3E-4F7B-9B1E-2D8C4F0A7E31")
private let controlUUID = CBUUID(string: "6A1D0002-5C3E-4F7B-9B1E-2D8C4F0A7E31")

// Mirrors BleEventKind.
private enum EventKind: UInt8 {
    case adapter = 1
    case peerFound = 2
    case connected = 3
    case disconnected = 4
    case received = 5
    case mtuChanged = 6
    case connectionInterval = 7
    case error = 8
    case advertising = 9
}

final class DuelBle: NSObject, CBPeripheralManagerDelegate, CBCentralManagerDelegate, CBPeripheralDelegate {
    static var shared: DuelBle?

    // Optional so the C function pointer is stored as an escaping value.
    private let callback: DuelBleEventCallback?
    let queue = DispatchQueue(label: "com.duel.core.ble", qos: .userInteractive)

    // Host (peripheral) state.
    private var peripheralManager: CBPeripheralManager?
    private var inputCharacteristic: CBMutableCharacteristic?
    private var controlCharacteristic: CBMutableCharacteristic?
    private var serviceAdded = false
    private var advertisement: [String: Any]?
    private var guest: CBCentral?
    private var guestSubscriptions = Set<CBUUID>()
    private var hostReady = false
    private var pendingNotifyInput: Data?
    private var pendingIndications: [Data] = []

    // Guest (central) state.
    private var centralManager: CBCentralManager?
    private var wantScan = false
    private var discovered: [UUID: CBPeripheral] = [:]
    private var host: CBPeripheral?
    private var hostInput: CBCharacteristic?
    private var hostControl: CBCharacteristic?
    private var guestReady = false
    private var pendingWriteInput: Data?

    init(callback: DuelBleEventCallback?) {
        self.callback = callback
        super.init()
    }

    // MARK: - Events to C#

    private func emit(_ kind: EventKind, channel: UInt8 = 0, value: Int32 = 0, value2: Int32 = 0,
                      peerId: String = "", name: String = "", data: Data = Data()) {
        var bytes = [UInt8]()
        bytes.reserveCapacity(16 + peerId.utf8.count + name.utf8.count + data.count)
        bytes.append(kind.rawValue)
        bytes.append(channel)
        appendInt32(&bytes, value)
        appendInt32(&bytes, value2)
        appendBlock(&bytes, Array(peerId.utf8))
        appendBlock(&bytes, Array(name.utf8))
        appendBlock(&bytes, [UInt8](data))
        bytes.withUnsafeBufferPointer { buffer in
            callback?(buffer.baseAddress, Int32(buffer.count))
        }
    }

    private func appendInt32(_ bytes: inout [UInt8], _ v: Int32) {
        let u = UInt32(bitPattern: v)
        bytes.append(UInt8(u & 0xFF))
        bytes.append(UInt8((u >> 8) & 0xFF))
        bytes.append(UInt8((u >> 16) & 0xFF))
        bytes.append(UInt8((u >> 24) & 0xFF))
    }

    private func appendBlock(_ bytes: inout [UInt8], _ block: [UInt8]) {
        let n = min(block.count, 0xFFFF)
        bytes.append(UInt8(n & 0xFF))
        bytes.append(UInt8((n >> 8) & 0xFF))
        bytes.append(contentsOf: block.prefix(n))
    }

    private func emitAdapter(_ state: CBManagerState) {
        switch state {
        case .poweredOn: emit(.adapter, value: 1)
        case .poweredOff: emit(.adapter, value: 0)
        case .unauthorized: emit(.adapter, value: -1)
        case .unsupported: emit(.adapter, value: -2)
        default: break
        }
    }

    // MARK: - Host (peripheral)

    func startAdvertising(localName: String) {
        advertisement = [
            CBAdvertisementDataLocalNameKey: localName,
            CBAdvertisementDataServiceUUIDsKey: [serviceUUID],
        ]
        if peripheralManager == nil {
            peripheralManager = CBPeripheralManager(delegate: self, queue: queue, options: nil)
        } else {
            publishAndAdvertise()
        }
    }

    func stopAdvertising() {
        advertisement = nil
        peripheralManager?.stopAdvertising()
    }

    private func publishAndAdvertise() {
        guard let pm = peripheralManager, pm.state == .poweredOn else { return }
        if !serviceAdded {
            let input = CBMutableCharacteristic(type: inputUUID, properties: [.writeWithoutResponse, .notify],
                                                value: nil, permissions: [.writeable])
            let control = CBMutableCharacteristic(type: controlUUID, properties: [.write, .indicate],
                                                  value: nil, permissions: [.writeable])
            let service = CBMutableService(type: serviceUUID, primary: true)
            service.characteristics = [input, control]
            inputCharacteristic = input
            controlCharacteristic = control
            serviceAdded = true
            pm.add(service) // Advertising starts in didAdd.
            return
        }
        if let adv = advertisement, !pm.isAdvertising {
            pm.startAdvertising(adv)
        }
    }

    func peripheralManagerDidUpdateState(_ peripheral: CBPeripheralManager) {
        emitAdapter(peripheral.state)
        if peripheral.state == .poweredOn {
            publishAndAdvertise()
        } else if hostReady {
            hostReady = false
            emit(.disconnected, value: -3, peerId: guest?.identifier.uuidString ?? "")
            guest = nil
            guestSubscriptions.removeAll()
        }
    }

    func peripheralManager(_ peripheral: CBPeripheralManager, didAdd service: CBService, error: Error?) {
        if let error = error {
            serviceAdded = false
            emit(.advertising, value: -1, name: error.localizedDescription)
            return
        }
        if let adv = advertisement {
            peripheral.startAdvertising(adv)
        }
    }

    func peripheralManagerDidStartAdvertising(_ peripheral: CBPeripheralManager, error: Error?) {
        if let error = error {
            emit(.advertising, value: -2, name: error.localizedDescription)
        } else {
            emit(.advertising, value: 1)
        }
    }

    func peripheralManager(_ peripheral: CBPeripheralManager, central: CBCentral,
                           didSubscribeTo characteristic: CBCharacteristic) {
        if let current = guest, current.identifier != central.identifier {
            return // One guest per court.
        }
        guest = central
        guestSubscriptions.insert(characteristic.uuid)
        peripheral.setDesiredConnectionLatency(.low, for: central)
        if !hostReady && guestSubscriptions.contains(inputUUID) && guestSubscriptions.contains(controlUUID) {
            hostReady = true
            let payload = Int32(central.maximumUpdateValueLength)
            emit(.connected, value: payload, value2: payload, peerId: central.identifier.uuidString)
        }
    }

    func peripheralManager(_ peripheral: CBPeripheralManager, central: CBCentral,
                           didUnsubscribeFrom characteristic: CBCharacteristic) {
        guard central.identifier == guest?.identifier else { return }
        guestSubscriptions.remove(characteristic.uuid)
        if hostReady {
            hostReady = false
            guest = nil
            guestSubscriptions.removeAll()
            pendingIndications.removeAll()
            pendingNotifyInput = nil
            emit(.disconnected, value: 0, peerId: central.identifier.uuidString)
        }
    }

    func peripheralManager(_ peripheral: CBPeripheralManager, didReceiveWrite requests: [CBATTRequest]) {
        var needsResponse = false
        for request in requests {
            if request.characteristic.uuid == controlUUID { needsResponse = true }
            guard let current = guest, current.identifier == request.central.identifier,
                  let value = request.value else { continue }
            let channel: UInt8 = request.characteristic.uuid == inputUUID ? 0 : 1
            emit(.received, channel: channel, data: value)
        }
        // Writes with response get exactly one answer, for the first request.
        if needsResponse, let first = requests.first {
            peripheral.respond(to: first, withResult: .success)
        }
    }

    private func hostSend(channel: Int, data: Data) {
        guard hostReady else { return }
        if channel == 0 {
            pendingNotifyInput = data // Newest only: an older input packet is superseded.
        } else {
            pendingIndications.append(data)
        }
        flushHost()
    }

    private func flushHost() {
        guard let pm = peripheralManager, let central = guest,
              let input = inputCharacteristic, let control = controlCharacteristic else { return }
        if let packet = pendingNotifyInput {
            if pm.updateValue(packet, for: input, onSubscribedCentrals: [central]) {
                pendingNotifyInput = nil
            } else {
                return // Queue full: peripheralManagerIsReady will call back.
            }
        }
        while let next = pendingIndications.first {
            if !pm.updateValue(next, for: control, onSubscribedCentrals: [central]) { return }
            pendingIndications.removeFirst()
        }
    }

    func peripheralManagerIsReady(toUpdateSubscribers peripheral: CBPeripheralManager) {
        flushHost()
    }

    // MARK: - Guest (central)

    func startScan() {
        wantScan = true
        if centralManager == nil {
            centralManager = CBCentralManager(delegate: self, queue: queue, options: nil)
        } else if centralManager?.state == .poweredOn {
            beginScan()
        }
    }

    func stopScan() {
        wantScan = false
        centralManager?.stopScan()
    }

    private func beginScan() {
        centralManager?.scanForPeripherals(withServices: [serviceUUID],
                                           options: [CBCentralManagerScanOptionAllowDuplicatesKey: true])
    }

    func centralManagerDidUpdateState(_ central: CBCentralManager) {
        emitAdapter(central.state)
        if central.state == .poweredOn && wantScan {
            beginScan()
        }
    }

    func centralManager(_ central: CBCentralManager, didDiscover peripheral: CBPeripheral,
                        advertisementData: [String: Any], rssi RSSI: NSNumber) {
        discovered[peripheral.identifier] = peripheral
        let name = (advertisementData[CBAdvertisementDataLocalNameKey] as? String) ?? peripheral.name ?? ""
        var serviceData = Data()
        if let all = advertisementData[CBAdvertisementDataServiceDataKey] as? [CBUUID: Data],
           let ours = all[serviceUUID] {
            serviceData = ours
        }
        emit(.peerFound, value: RSSI.int32Value, peerId: peripheral.identifier.uuidString, name: name, data: serviceData)
    }

    func connect(peerId: String) {
        guard let central = centralManager, let uuid = UUID(uuidString: peerId) else {
            emit(.disconnected, value: -1, peerId: peerId, name: "unknown court")
            return
        }
        guard let peripheral = discovered[uuid] ?? central.retrievePeripherals(withIdentifiers: [uuid]).first else {
            emit(.disconnected, value: -1, peerId: peerId, name: "court not found")
            return
        }
        stopScan()
        host = peripheral
        guestReady = false
        peripheral.delegate = self
        central.connect(peripheral, options: nil)
    }

    func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
        peripheral.discoverServices([serviceUUID])
    }

    func centralManager(_ central: CBCentralManager, didFailToConnect peripheral: CBPeripheral, error: Error?) {
        emit(.disconnected, value: -2, peerId: peripheral.identifier.uuidString,
             name: error?.localizedDescription ?? "connection failed")
    }

    func centralManager(_ central: CBCentralManager, didDisconnectPeripheral peripheral: CBPeripheral, error: Error?) {
        guard peripheral.identifier == host?.identifier else { return }
        resetGuestLink()
        emit(.disconnected, value: 0, peerId: peripheral.identifier.uuidString, name: error?.localizedDescription ?? "")
    }

    private func resetGuestLink() {
        guestReady = false
        hostInput = nil
        hostControl = nil
        pendingWriteInput = nil
    }

    func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: Error?) {
        guard error == nil, let service = peripheral.services?.first(where: { $0.uuid == serviceUUID }) else {
            emit(.error, value: 1, name: error?.localizedDescription ?? "court service not found")
            centralManager?.cancelPeripheralConnection(peripheral)
            return
        }
        peripheral.discoverCharacteristics([inputUUID, controlUUID], for: service)
    }

    func peripheral(_ peripheral: CBPeripheral, didDiscoverCharacteristicsFor service: CBService, error: Error?) {
        for characteristic in service.characteristics ?? [] {
            if characteristic.uuid == inputUUID { hostInput = characteristic }
            if characteristic.uuid == controlUUID { hostControl = characteristic }
        }
        guard error == nil, let input = hostInput, let control = hostControl else {
            emit(.error, value: 2, name: error?.localizedDescription ?? "court characteristics not found")
            centralManager?.cancelPeripheralConnection(peripheral)
            return
        }
        peripheral.setNotifyValue(true, for: input)
        peripheral.setNotifyValue(true, for: control)
    }

    func peripheral(_ peripheral: CBPeripheral, didUpdateNotificationStateFor characteristic: CBCharacteristic,
                    error: Error?) {
        if let error = error {
            emit(.error, value: 3, name: error.localizedDescription)
            return
        }
        if !guestReady, hostInput?.isNotifying == true, hostControl?.isNotifying == true {
            guestReady = true
            // One ATT payload per fragment in both directions (no long writes), so Android hosts need no
            // prepared-write handling.
            let payload = Int32(peripheral.maximumWriteValueLength(for: .withoutResponse))
            emit(.connected, value: payload, value2: payload, peerId: peripheral.identifier.uuidString)
        }
    }

    func peripheral(_ peripheral: CBPeripheral, didUpdateValueFor characteristic: CBCharacteristic, error: Error?) {
        guard error == nil, let value = characteristic.value else { return }
        let channel: UInt8 = characteristic.uuid == inputUUID ? 0 : 1
        emit(.received, channel: channel, data: value)
    }

    func peripheral(_ peripheral: CBPeripheral, didWriteValueFor characteristic: CBCharacteristic, error: Error?) {
        if let error = error {
            emit(.error, value: 4, name: error.localizedDescription)
        }
    }

    func peripheralIsReady(toSendWriteWithoutResponse peripheral: CBPeripheral) {
        if let packet = pendingWriteInput, let input = hostInput {
            pendingWriteInput = nil
            peripheral.writeValue(packet, for: input, type: .withoutResponse)
        }
    }

    private func guestSend(channel: Int, data: Data) {
        guard guestReady, let peripheral = host else { return }
        if channel == 0 {
            guard let input = hostInput else { return }
            if peripheral.canSendWriteWithoutResponse {
                peripheral.writeValue(data, for: input, type: .withoutResponse)
            } else {
                pendingWriteInput = data // Flow control: keep only the newest packet.
            }
        } else if let control = hostControl {
            // CoreBluetooth queues writes with response and sends them in order.
            peripheral.writeValue(data, for: control, type: .withResponse)
        }
    }

    // MARK: - Shared

    func send(channel: Int, data: Data) {
        if hostReady {
            hostSend(channel: channel, data: data)
        } else if guestReady {
            guestSend(channel: channel, data: data)
        }
    }

    func disconnect() {
        if let peripheral = host {
            centralManager?.cancelPeripheralConnection(peripheral)
        }
        host = nil
        resetGuestLink()
        stopScan()
        advertisement = nil
        peripheralManager?.stopAdvertising()
        // A peripheral cannot drop a central; removing the service ends the session for the guest.
        peripheralManager?.removeAllServices()
        serviceAdded = false
        inputCharacteristic = nil
        controlCharacteristic = nil
        guest = nil
        guestSubscriptions.removeAll()
        hostReady = false
        pendingIndications.removeAll()
        pendingNotifyInput = nil
    }
}

// MARK: - C entry points for DllImport("__Internal")

private func text(_ bytes: UnsafePointer<UInt8>?, _ length: Int32) -> String {
    guard let bytes = bytes, length > 0 else { return "" }
    return String(decoding: UnsafeBufferPointer(start: bytes, count: Int(length)), as: UTF8.self)
}

@_cdecl("duel_ble_init")
public func duel_ble_init(_ callback: DuelBleEventCallback?) {
    guard let callback = callback, DuelBle.shared == nil else { return }
    DuelBle.shared = DuelBle(callback: callback)
}

@_cdecl("duel_ble_start_advertising")
public func duel_ble_start_advertising(_ name: UnsafePointer<UInt8>?, _ nameLength: Int32,
                                       _ serviceData: UnsafePointer<UInt8>?, _ serviceDataLength: Int32) {
    // iOS peripherals cannot advertise service data; the court code travels in the local name.
    let localName = text(name, nameLength)
    guard let ble = DuelBle.shared else { return }
    ble.queue.async { ble.startAdvertising(localName: localName) }
}

@_cdecl("duel_ble_stop_advertising")
public func duel_ble_stop_advertising() {
    guard let ble = DuelBle.shared else { return }
    ble.queue.async { ble.stopAdvertising() }
}

@_cdecl("duel_ble_start_scan")
public func duel_ble_start_scan() {
    guard let ble = DuelBle.shared else { return }
    ble.queue.async { ble.startScan() }
}

@_cdecl("duel_ble_stop_scan")
public func duel_ble_stop_scan() {
    guard let ble = DuelBle.shared else { return }
    ble.queue.async { ble.stopScan() }
}

@_cdecl("duel_ble_connect")
public func duel_ble_connect(_ peerId: UnsafePointer<UInt8>?, _ length: Int32) {
    let id = text(peerId, length)
    guard let ble = DuelBle.shared else { return }
    ble.queue.async { ble.connect(peerId: id) }
}

@_cdecl("duel_ble_disconnect")
public func duel_ble_disconnect() {
    guard let ble = DuelBle.shared else { return }
    ble.queue.async { ble.disconnect() }
}

@_cdecl("duel_ble_send")
public func duel_ble_send(_ channel: Int32, _ bytes: UnsafePointer<UInt8>?, _ length: Int32) {
    guard let bytes = bytes, length > 0, let ble = DuelBle.shared else { return }
    let data = Data(bytes: bytes, count: Int(length)) // Copied before the hop to the BLE queue.
    let ch = Int(channel)
    ble.queue.async { ble.send(channel: ch, data: data) }
}
