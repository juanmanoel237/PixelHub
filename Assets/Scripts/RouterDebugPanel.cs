using System.Text;
using UnityEngine;
using Laps.Core;
using Laps.Routing;

/// <summary>
/// Panneau debug routage (F7) : univers DMX, canaux actifs, entités eHuB, sonde pixel.
/// </summary>
public class RouterDebugPanel : MonoBehaviour
{
    private RoutingEngine _routing;
    private EHubReceiver _ehubReceiver;
    private bool _show;
    private int _selectedUniverseKey = -1;
    private int _probeLedIndex;
    private float _refreshTimer;
    private RoutingDebugSnapshot _snapshot = new RoutingDebugSnapshot();
    private Vector2 _universeScroll;

    private void Awake()
    {
        _routing = FindObjectOfType<RoutingEngine>();
        _ehubReceiver = FindObjectOfType<EHubReceiver>();
    }

    private void Update()
    {
        if (!_show) return;

        _refreshTimer += Time.unscaledDeltaTime;
        if (_refreshTimer < 0.15f) return;
        _refreshTimer = 0f;

        if (_routing != null && _routing.TryGetDebugSnapshot(out RoutingDebugSnapshot snap))
            _snapshot = snap;
    }

    private void OnGUI()
    {
        // F7 via IMGUI : plus fiable que Input.GetKeyDown quand la Game view a le focus.
        Event e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.F7)
        {
            TogglePanel();
            e.Use();
        }

        DrawToggleTab();

        if (!_show) return;

        GUI.depth = 200;

        const int panelW = 460;
        const int margin = 12;
        int panelH = Mathf.Min(Screen.height - margin * 2, 640);
        float x = (Screen.width - panelW) * 0.5f;
        float y = (Screen.height - panelH) * 0.5f;

        GUI.Box(new Rect(x, y, panelW, panelH), "Routeur — Debug DMX (F7)");

        float lineY = y + 22;
        float innerX = x + 10;
        float innerW = panelW - 20;

        if (GUI.Button(new Rect(x + panelW - 78, y + 4, 70, 20), "Fermer"))
            _show = false;

        DrawStatus(innerX, ref lineY, innerW);
        lineY += 6;
        DrawEntitySection(innerX, ref lineY, innerW);
        lineY += 6;
        DrawPixelProbe(innerX, ref lineY, innerW);
        lineY += 6;
        DrawUniverseList(innerX, ref lineY, innerW, y + panelH - 12);
        lineY += 6;
        DrawChannelGrid(innerX, ref lineY, innerW);

        GUI.depth = 0;
    }

    private void DrawToggleTab()
    {
        GUI.depth = 150;
        const float tabW = 160;
        const float tabH = 24;
        float x = Screen.width - tabW - 12;
        float y = 12;

        if (GUI.Button(new Rect(x, y, tabW, tabH), _show ? "DMX Debug ON (F7)" : "DMX Debug (F7)"))
            TogglePanel();

        GUI.depth = 0;
    }

    private void TogglePanel()
    {
        _show = !_show;
        _refreshTimer = 999f;
        if (_routing != null && _routing.TryGetDebugSnapshot(out RoutingDebugSnapshot snap))
            _snapshot = snap;
        Debug.Log($"[RouterDebugPanel] Panneau DMX {(_show ? "ouvert" : "fermé")} — F7 ou bouton en haut à droite.");
    }

    public void ToggleVisible() => TogglePanel();

    private void DrawStatus(float x, ref float y, float w)
    {
        string mode = _snapshot.Mode switch
        {
            RoutingDebugMode.Pixel => "Pixel (buffer 128×128)",
            RoutingDebugMode.Entity => "Entités eHuB (CSV)",
            _ => "Inactif"
        };

        GUI.Label(new Rect(x, y, w, 18), $"Mode routage : {mode}");
        y += 18;

        string artNet = _snapshot.HardwareOutputEnabled ? "ENVOI actif (mur)" : "Pas d'envoi Art-Net (client ou solo)";
        GUI.color = _snapshot.HardwareOutputEnabled ? Color.white : new Color(1f, 0.85f, 0.4f);
        GUI.Label(new Rect(x, y, w, 18), $"Art-Net : {artNet}");
        GUI.color = Color.white;
        y += 18;

        GUI.Label(new Rect(x, y, w, 18),
            $"Paquets/s {_snapshot.PacketsPerSecond:F1} | Total {_snapshot.PacketsSentTotal} | Route {_snapshot.RoutingFps:F0} Hz");
        y += 18;

        GUI.Label(new Rect(x, y, w, 18),
            $"Univers actifs : {_snapshot.ActiveUniverseCount} | CSV entités : {ConfigManager.EntityMap?.Count ?? 0}");
        y += 18;

        if (_ehubReceiver != null)
        {
            int port = ConfigManager.Config?.network?.ehubProtocolPort ?? 9001;
            GUI.Label(new Rect(x, y, w, 18),
                $"eHuB RX port {port} : {_ehubReceiver.UpdatesReceived} màj, {_ehubReceiver.LastEntityCount} entités/dernier paquet");
            y += 18;
        }

        if (_snapshot.HasFirstPixelAddress)
        {
            ref LEDAddress a = ref _snapshot.FirstPixelAddress;
            GUI.Label(new Rect(x, y, w, 18),
                $"Pixel[0] → ctrl {a.controllerIndex} univ {a.universe} canal DMX {a.channel + 1}");
            y += 18;
        }
    }

    private void DrawEntitySection(float x, ref float y, float w)
    {
        if (_snapshot.Mode != RoutingDebugMode.Entity) return;

        GUI.Label(new Rect(x, y, w, 18),
            $"Entités : {_snapshot.EntityReceived} reçues | {_snapshot.EntityMapped} mappées | {_snapshot.EntityUnmapped} ORPHELINES");
        y += 18;

        if (_snapshot.EntityUnmapped > 0)
        {
            GUI.color = new Color(1f, 0.45f, 0.45f);
            var sb = new StringBuilder("IDs non mappés (échantillon) : ");
            for (int i = 0; i < _snapshot.UnmappedEntityIds.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(_snapshot.UnmappedEntityIds[i]);
            }
            if (_snapshot.EntityUnmapped > _snapshot.UnmappedEntityIds.Length)
                sb.Append("…");
            GUI.Label(new Rect(x, y, w, 36), sb.ToString());
            GUI.color = Color.white;
            y += 38;
        }
    }

    private void DrawPixelProbe(float x, ref float y, float w)
    {
        var cfg = ConfigManager.Config?.mapping;
        int ledCount = cfg?.ledCount ?? 0;
        int screenW = cfg?.screenWidth > 0 ? cfg.screenWidth : 128;
        if (ledCount <= 0 || _routing?.Mapping?.PixelMap == null) return;

        GUI.Label(new Rect(x, y, w, 18), "Sonde pixel → adresse DMX");
        y += 18;

        _probeLedIndex = Mathf.RoundToInt(GUI.HorizontalSlider(
            new Rect(x, y, w - 120, 18), _probeLedIndex, 0, ledCount - 1));
        GUI.Label(new Rect(x + w - 112, y - 2, 112, 18), $"index {_probeLedIndex}");
        y += 22;

        int idx = Mathf.Clamp(_probeLedIndex, 0, _routing.Mapping.PixelMap.Length - 1);
        int px = idx % screenW;
        int py = idx / screenW;
        ref LEDAddress addr = ref _routing.Mapping.PixelMap[idx];

        if (addr.controllerIndex < 0)
        {
            GUI.Label(new Rect(x, y, w, 18), $"({px},{py}) — non mappé");
            y += 18;
            return;
        }

        string ctrlIp = "?";
        var controllers = ConfigManager.Config?.network?.controllers;
        if (controllers != null && addr.controllerIndex < controllers.Length)
            ctrlIp = controllers[addr.controllerIndex].ip ?? "?";

        GUI.Label(new Rect(x, y, w, 18),
            $"({px},{py}) → ctrl {addr.controllerIndex} ({ctrlIp}) univ {addr.universe} canaux DMX {addr.channel + 1}–{addr.channel + 3}");
        y += 18;
    }

    private void DrawUniverseList(float x, ref float y, float w, float maxY)
    {
        if (_snapshot.Universes == null || _snapshot.Universes.Count == 0)
        {
            GUI.Label(new Rect(x, y, w, 18), "Aucun univers DMX actif — testez D+R/G/B ou mode E + Emitter Hub.");
            y += 18;
            return;
        }

        GUI.Label(new Rect(x, y, w, 18), "Univers (cliquer pour inspecter les 512 canaux) :");
        y += 20;

        float listH = Mathf.Min(72, maxY - y - 120);
        if (listH < 24) listH = 24;

        _universeScroll = GUI.BeginScrollView(
            new Rect(x, y, w, listH), _universeScroll, new Rect(0, 0, w - 20, _snapshot.Universes.Count * 22));

        float uy = 0;
        for (int i = 0; i < _snapshot.Universes.Count; i++)
        {
            var u = _snapshot.Universes[i];
            string label = $"Ctrl {u.controllerIndex} ({u.controllerIp}) univ {u.universe} — {u.activeChannelCount} canaux actifs";
            if (u.firstActiveChannel >= 0)
                label += $", 1er=DMX {u.firstActiveChannel + 1}";

            bool selected = u.key == _selectedUniverseKey;
            if (selected) GUI.backgroundColor = new Color(0.35f, 0.65f, 1f);
            if (GUI.Button(new Rect(0, uy, w - 24, 20), label))
                _selectedUniverseKey = u.key;
            GUI.backgroundColor = Color.white;
            uy += 22;
        }

        GUI.EndScrollView();
        y += listH + 4;

        if (_selectedUniverseKey < 0 && _snapshot.Universes.Count > 0)
            _selectedUniverseKey = _snapshot.Universes[0].key;
    }

    private void DrawChannelGrid(float x, ref float y, float w)
    {
        if (_selectedUniverseKey < 0 ||
            !_snapshot.DmxBuffers.TryGetValue(_selectedUniverseKey, out byte[] buf) ||
            buf == null)
        {
            GUI.Label(new Rect(x, y, w, 18), "Sélectionnez un univers pour voir les canaux.");
            y += 18;
            return;
        }

        int ctrl = _selectedUniverseKey >> 16;
        int univ = _selectedUniverseKey & 0xFFFF;
        GUI.Label(new Rect(x, y, w, 18), $"Grille DMX — ctrl {ctrl} univers {univ} (512 canaux, 32×16)");
        y += 20;

        const int cols = 32;
        const int rows = 16;
        float cellW = (w - 4) / cols;
        float cellH = 8f;
        float gridH = rows * cellH;

        for (int i = 0; i < 512; i++)
        {
            int col = i % cols;
            int row = i / cols;
            float cx = x + col * cellW;
            float cy = y + row * cellH;
            byte v = buf[i];
            GUI.color = v > 0
                ? new Color(v / 255f, v / 255f, v / 255f, 1f)
                : new Color(0.12f, 0.12f, 0.14f, 1f);
            GUI.DrawTexture(new Rect(cx, cy, cellW - 1, cellH - 1), Texture2D.whiteTexture);
        }

        GUI.color = Color.white;
        y += gridH + 4;

        DrawChannelSamples(x, ref y, w, buf);
    }

    private static void DrawChannelSamples(float x, ref float y, float w, byte[] buf)
    {
        var sb = new StringBuilder("Canaux actifs (échantillon) : ");
        int shown = 0;
        for (int i = 0; i < buf.Length && shown < 12; i++)
        {
            if (buf[i] == 0) continue;
            if (shown > 0) sb.Append(" | ");
            sb.Append($"DMX {i + 1}={buf[i]}");
            shown++;
        }
        if (shown == 0) sb.Append("aucun");
        GUI.Label(new Rect(x, y, w, 36), sb.ToString());
        y += 38;
    }
}
