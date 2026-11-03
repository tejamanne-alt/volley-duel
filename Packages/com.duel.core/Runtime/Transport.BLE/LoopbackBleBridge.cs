using System.Collections.Concurrent;

namespace Duel.Core.Transport.BLE
{
    /// <summary>
    /// Two in-process bridges that behave like a BLE link: adverts in the iOS or Android style,
    /// ATT-sized payloads, and ordered delivery. Events are queued on a lock-free queue, as the native
    /// bridges do. Used by tests and in the editor, where there is no radio.
    /// </summary>
    public sealed class LoopbackBleBridge : IBleBridge
    {
        readonly ConcurrentQueue<BleEvent> events = new ConcurrentQueue<BleEvent>();
        LoopbackBleBridge other;
        string advertisedName;
        byte[] advertisedData;
        bool advertising;
        bool scanning;
        bool connected;

        /// <summary>Advertise like an Android phone (code in service data) instead of iOS (code in the local name).</summary>
        public bool AndroidStyleAdvert;

        /// <summary>Largest ATT payload after MTU negotiation (20 = the default MTU of 23).</summary>
        public int AttPayload = 20;

        public bool CanAdvertise { get; set; } = true;

        public string Id { get; }

        LoopbackBleBridge(string id) => Id = id;

        public static void CreatePair(out LoopbackBleBridge a, out LoopbackBleBridge b)
        {
            a = new LoopbackBleBridge("loopback-a");
            b = new LoopbackBleBridge("loopback-b");
            a.other = b;
            b.other = a;
            a.events.Enqueue(new BleEvent { Kind = BleEventKind.Adapter, Value = 1 });
            b.events.Enqueue(new BleEvent { Kind = BleEventKind.Adapter, Value = 1 });
        }

        public void StartAdvertising(string localName, byte[] serviceData)
        {
            advertising = true;
            advertisedName = AndroidStyleAdvert ? null : localName;
            advertisedData = AndroidStyleAdvert ? serviceData : null;
            events.Enqueue(new BleEvent { Kind = BleEventKind.Advertising, Value = 1 });
            if (other.scanning) other.Discover(this);
        }

        public void StopAdvertising() => advertising = false;

        public void StartScan()
        {
            scanning = true;
            if (other.advertising) Discover(other);
        }

        public void StopScan() => scanning = false;

        void Discover(LoopbackBleBridge host)
        {
            events.Enqueue(new BleEvent { Kind = BleEventKind.PeerFound, PeerId = host.Id, Name = host.advertisedName, Data = host.advertisedData, Value = -48 });
        }

        public void Connect(string peerId)
        {
            if (peerId != other.Id || !other.advertising)
            {
                events.Enqueue(new BleEvent { Kind = BleEventKind.Disconnected, PeerId = peerId, Value = 133 });
                return;
            }
            connected = true;
            other.connected = true;
            int payload = System.Math.Min(AttPayload, other.AttPayload);
            events.Enqueue(new BleEvent { Kind = BleEventKind.Connected, PeerId = other.Id, Value = payload, Value2 = payload });
            other.events.Enqueue(new BleEvent { Kind = BleEventKind.Connected, PeerId = Id, Value = payload, Value2 = payload });
            events.Enqueue(new BleEvent { Kind = BleEventKind.ConnectionInterval, Value = 15000 });
            other.events.Enqueue(new BleEvent { Kind = BleEventKind.ConnectionInterval, Value = 15000 });
        }

        public void Disconnect()
        {
            if (!connected) return;
            connected = false;
            other.connected = false;
            other.events.Enqueue(new BleEvent { Kind = BleEventKind.Disconnected, PeerId = Id, Value = 19 });
        }

        public void SendInput(byte[] packet, int length) => Deliver(0, packet, length);

        public void SendControl(byte[] fragment, int length) => Deliver(1, fragment, length);

        void Deliver(byte channel, byte[] bytes, int length)
        {
            if (!connected) return;
            int payload = System.Math.Min(AttPayload, other.AttPayload);
            if (length > payload)
            {
                events.Enqueue(new BleEvent { Kind = BleEventKind.Error, Value = 13, Name = "ATT payload larger than the MTU" });
                return;
            }
            var copy = new byte[length];
            System.Array.Copy(bytes, copy, length);
            other.events.Enqueue(new BleEvent { Kind = BleEventKind.Received, Channel = channel, Data = copy });
        }

        public bool TryDequeue(out BleEvent e) => events.TryDequeue(out e);

        public void Dispose() => Disconnect();
    }
}
