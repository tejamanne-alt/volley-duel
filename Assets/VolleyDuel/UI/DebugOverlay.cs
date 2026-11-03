using System.Text;
using Duel.Core.Net;
using TMPro;
using UnityEngine;

namespace VolleyDuel.UI
{
    /// <summary>
    /// Netplay telemetry (spec): RTT, jitter, loss, the rollback histogram and the desync count, plus the
    /// input delay and transport link details. It is toggled in Settings and always shown in Training.
    /// </summary>
    public sealed class DebugOverlay : MonoBehaviour
    {
        TextMeshProUGUI text;
        readonly StringBuilder sb = new StringBuilder(512);
        float refresh;

        public static DebugOverlay Build(Transform safeArea)
        {
            RectTransform r = UiKit.At("DebugOverlay", safeArea, new Vector2(0f, 0f), new Vector2(900f, 260f), new Vector2(20f, 120f));
            DebugOverlay d = r.gameObject.AddComponent<DebugOverlay>();
            d.text = UiKit.Label(r, string.Empty, 26f, TextAlignmentOptions.BottomLeft, new Color(0.7f, 1f, 0.7f, 0.9f));
            return d;
        }

        public void Show(IMatchRunner runner, ITransport transport)
        {
            refresh -= Time.unscaledDeltaTime;
            if (refresh > 0f) return;
            refresh = 0.25f;
            sb.Length = 0;
            sb.Append("frame ").Append(runner.Truth.Tick).Append("  world ").Append(runner.Truth.World);
            if (runner is NetMatchRunner net)
            {
                NetStats s = net.Session.Stats;
                sb.Append("\nRTT ").Append(s.RttMicros / 1000).Append(" ms  jitter ").Append(s.JitterMicros / 1000).Append(" ms  loss ").Append(s.LossPermille / 10f).Append("%  delay ").Append(net.Session.InputDelay);
                sb.Append("\nrollbacks ").Append(s.Rollbacks).Append(" (max ").Append(s.MaxRollbackDepth).Append(")  desyncs ").Append(s.Desyncs).Append("  stalls ").Append(s.Stalls);
                sb.Append("\nhistogram");
                for (int i = 1; i < s.RollbackHistogram.Length; i++) sb.Append(' ').Append(i).Append(':').Append(s.RollbackHistogram[i]);
            }
            if (transport != null)
            {
                TransportStats t = transport.Stats;
                sb.Append('\n').Append(transport.Name).Append("  mtu ").Append(t.Mtu).Append("  interval ").Append(t.ConnectionIntervalMicros / 1000f).Append(" ms  sent ").Append(t.PacketsSent).Append("  recv ").Append(t.PacketsReceived);
            }
            text.text = sb.ToString();
        }
    }
}
