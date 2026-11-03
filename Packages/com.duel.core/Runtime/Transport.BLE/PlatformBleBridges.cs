using System;
using System.Collections.Concurrent;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;
using UnityEngine.Android;
#endif

namespace Duel.Core.Transport.BLE
{
    /// <summary>Picks the native bridge for the running platform.</summary>
    public static class BleBridges
    {
        /// <summary>The platform's BLE bridge, or null where there is no radio bridge (editor, desktop, headless).</summary>
        public static IBleBridge CreateForPlatform()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return new IosBleBridge();
#elif UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidBleBridge();
#else
            return null;
#endif
        }

        /// <summary>
        /// Android 12+ needs scan, advertise and connect permissions; older Android needs location for
        /// scanning. iOS asks on first use (NSBluetoothAlwaysUsageDescription is added at build time).
        /// Call from the lobby before hosting or joining.
        /// </summary>
        public static void RequestPermissions()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidBleBridge.RequestPermissions();
#endif
        }

        /// <summary>Whether the permissions above are granted (always true off Android).</summary>
        public static bool HasPermissions()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return AndroidBleBridge.HasPermissions();
#else
            return true;
#endif
        }
    }

    /// <summary>Lock-free hand-off from radio threads to the main thread, shared by the platform bridges.</summary>
    sealed class BleEventQueue
    {
        readonly ConcurrentQueue<BleEvent> queue = new ConcurrentQueue<BleEvent>();

        public void Push(ReadOnlySpan<byte> encoded)
        {
            if (BleEventCodec.TryDecode(encoded, out BleEvent e)) queue.Enqueue(e);
        }

        public void PushBatch(ReadOnlySpan<byte> batch) => BleEventCodec.DecodeBatch(batch, e => queue.Enqueue(e));

        public bool TryDequeue(out BleEvent e) => queue.TryDequeue(out e);
    }

#if UNITY_IOS && !UNITY_EDITOR
    /// <summary>
    /// CoreBluetooth bridge: Plugins/iOS/DuelBle.swift through DllImport("__Internal"). Swift calls back on
    /// its CoreBluetooth queue with an encoded event; the callback only copies the bytes into the lock-free
    /// queue, which Poll drains on the main thread.
    /// </summary>
    sealed class IosBleBridge : IBleBridge
    {
        delegate void EventCallback(IntPtr bytes, int length);

        [DllImport("__Internal")] static extern void duel_ble_init(EventCallback callback);
        [DllImport("__Internal")] static extern void duel_ble_start_advertising(byte[] localName, int localNameLength, byte[] serviceData, int serviceDataLength);
        [DllImport("__Internal")] static extern void duel_ble_stop_advertising();
        [DllImport("__Internal")] static extern void duel_ble_start_scan();
        [DllImport("__Internal")] static extern void duel_ble_stop_scan();
        [DllImport("__Internal")] static extern void duel_ble_connect(byte[] peerId, int peerIdLength);
        [DllImport("__Internal")] static extern void duel_ble_disconnect();
        [DllImport("__Internal")] static extern void duel_ble_send(int channel, byte[] data, int length);

        static readonly BleEventQueue Queue = new BleEventQueue();
        // Held in a static so the delegate outlives every native call.
        static readonly EventCallback Callback = OnNativeEvent;
        static bool initialized;

        public IosBleBridge()
        {
            if (initialized) return;
            initialized = true;
            duel_ble_init(Callback);
        }

        [AOT.MonoPInvokeCallback(typeof(EventCallback))]
        static void OnNativeEvent(IntPtr bytes, int length)
        {
            if (bytes == IntPtr.Zero || length <= 0) return;
            var copy = new byte[length];
            Marshal.Copy(bytes, copy, 0, length);
            Queue.Push(copy);
        }

        public bool CanAdvertise => true;

        public void StartAdvertising(string localName, byte[] serviceData)
        {
            byte[] name = System.Text.Encoding.UTF8.GetBytes(localName ?? string.Empty);
            byte[] data = serviceData ?? Array.Empty<byte>();
            duel_ble_start_advertising(name, name.Length, data, data.Length);
        }

        public void StopAdvertising() => duel_ble_stop_advertising();

        public void StartScan() => duel_ble_start_scan();

        public void StopScan() => duel_ble_stop_scan();

        public void Connect(string peerId)
        {
            byte[] id = System.Text.Encoding.UTF8.GetBytes(peerId ?? string.Empty);
            duel_ble_connect(id, id.Length);
        }

        public void Disconnect() => duel_ble_disconnect();

        public void SendInput(byte[] packet, int length) => duel_ble_send(0, packet, length);

        public void SendControl(byte[] fragment, int length) => duel_ble_send(1, fragment, length);

        public bool TryDequeue(out BleEvent e) => Queue.TryDequeue(out e);

        public void Dispose() => duel_ble_disconnect();
    }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// Android BLE bridge: Plugins/Android/DuelBle.kt through AndroidJavaObject. Kotlin queues encoded events
    /// on a lock-free ConcurrentLinkedQueue; one JNI call per frame drains them as a batch.
    /// </summary>
    sealed class AndroidBleBridge : IBleBridge
    {
        static readonly string[] Modern = { "android.permission.BLUETOOTH_SCAN", "android.permission.BLUETOOTH_ADVERTISE", "android.permission.BLUETOOTH_CONNECT" };
        static readonly string[] Legacy = { "android.permission.ACCESS_FINE_LOCATION" };

        readonly AndroidJavaObject plugin;
        readonly BleEventQueue queue = new BleEventQueue();
        readonly sbyte[] inputBuffer = new sbyte[20];

        public AndroidBleBridge()
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                plugin = new AndroidJavaObject("com.duel.core.ble.DuelBle", activity);
            }
        }

        static int SdkInt()
        {
            using (var version = new AndroidJavaClass("android.os.Build$VERSION")) return version.GetStatic<int>("SDK_INT");
        }

        static string[] Needed() => SdkInt() >= 31 ? Modern : Legacy;

        public static bool HasPermissions()
        {
            foreach (string p in Needed())
            {
                if (!Permission.HasUserAuthorizedPermission(p)) return false;
            }
            return true;
        }

        public static void RequestPermissions()
        {
            if (!HasPermissions()) Permission.RequestUserPermissions(Needed());
        }

        public bool CanAdvertise => plugin.Call<bool>("canAdvertise");

        public void StartAdvertising(string localName, byte[] serviceData) => plugin.Call("startAdvertising", localName ?? string.Empty, ToSigned(serviceData ?? Array.Empty<byte>(), serviceData?.Length ?? 0));

        public void StopAdvertising() => plugin.Call("stopAdvertising");

        public void StartScan() => plugin.Call("startScan");

        public void StopScan() => plugin.Call("stopScan");

        public void Connect(string peerId) => plugin.Call("connect", peerId ?? string.Empty);

        public void Disconnect() => plugin.Call("disconnect");

        public void SendInput(byte[] packet, int length)
        {
            sbyte[] buffer = length == inputBuffer.Length ? inputBuffer : new sbyte[length];
            Buffer.BlockCopy(packet, 0, buffer, 0, length);
            plugin.Call("send", 0, buffer);
        }

        public void SendControl(byte[] fragment, int length) => plugin.Call("send", 1, ToSigned(fragment, length));

        public bool TryDequeue(out BleEvent e)
        {
            if (queue.TryDequeue(out e)) return true;
            sbyte[] batch = plugin.Call<sbyte[]>("poll");
            if (batch == null || batch.Length == 0) return false;
            var bytes = new byte[batch.Length];
            Buffer.BlockCopy(batch, 0, bytes, 0, batch.Length);
            queue.PushBatch(bytes);
            return queue.TryDequeue(out e);
        }

        static sbyte[] ToSigned(byte[] bytes, int length)
        {
            var s = new sbyte[length];
            Buffer.BlockCopy(bytes, 0, s, 0, length);
            return s;
        }

        public void Dispose()
        {
            plugin.Call("disconnect");
            plugin.Dispose();
        }
    }
#endif
}
