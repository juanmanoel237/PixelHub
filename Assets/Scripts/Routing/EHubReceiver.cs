using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using Laps.Core;

namespace Laps.Routing
{
    /// <summary>
    /// Récepteur UDP du protocole eHuB (P7).
    /// - Reçoit "config" (type=1) et "update" (type=2)
    /// - Décompresse le payload GZip
    /// - Expose un state entité (id → couleur) via IEntityStateProvider
    /// </summary>
    public class EHubReceiver : MonoBehaviour, IStateProvider, IEntityStateProvider
    {
        [Header("UDP")]
        [Tooltip("Si vide: écoute sur toutes les interfaces.")]
        [SerializeField] private string _listenIp = "";

        [Tooltip("Si 0: utilise ConfigManager.Config.network.eHubPort")]
        [SerializeField] private int _listenPortOverride = 0;

        [Header("eHuB")]
        [Tooltip("Univers eHuB à écouter (0-255). 0 = accepte tous.")]
        [SerializeField] private int _ehubUniverseFilter = 0;

        private UdpClient _udp;
        private Thread _thread;
        private volatile bool _running;

        private readonly object _lock = new object();
        private EntityColor[] _latestEntities = Array.Empty<EntityColor>();

        // Ranges décrits par le message config (si update ne contient pas les IDs)
        private readonly List<RangeMap> _ranges = new List<RangeMap>(64);

        private int _updatesReceived;
        private int _lastEntityCount;

        public int UpdatesReceived => _updatesReceived;
        public int LastEntityCount => _lastEntityCount;

        private static readonly Color32[] EmptyPixels = Array.Empty<Color32>();
        private static readonly LyreState[] EmptyLyres = Array.Empty<LyreState>();

        public Color32[] GetState() => EmptyPixels;
        public LyreState[] GetLyreStates() => EmptyLyres;

        public IReadOnlyList<EntityColor> GetEntityState()
        {
            lock (_lock) return _latestEntities;
        }

        private void Awake()
        {
            ConfigManager.OnConfigReloaded += Restart;
        }

        private void Start()
        {
            StartListener();
        }

        private void OnDestroy()
        {
            ConfigManager.OnConfigReloaded -= Restart;
            StopListener();
        }

        private void Restart()
        {
            StopListener();
            StartListener();
        }

        private void StartListener()
        {
            int port = _listenPortOverride > 0
                ? _listenPortOverride
                : (ConfigManager.Config?.network?.ehubProtocolPort ?? 9001);
            if (port <= 0) port = 9001;

            try
            {
                IPEndPoint ep = string.IsNullOrWhiteSpace(_listenIp)
                    ? new IPEndPoint(IPAddress.Any, port)
                    : new IPEndPoint(IPAddress.Parse(_listenIp), port);

                _udp = new UdpClient(ep);
                _udp.Client.ReceiveBufferSize = 1_048_576;

                _running = true;
                _thread = new Thread(ReceiveLoop)
                {
                    Name = "PixelHub-eHuB",
                    IsBackground = true,
                    Priority = System.Threading.ThreadPriority.AboveNormal
                };
                _thread.Start();

                Debug.Log($"[EHubReceiver] Écoute eHuB sur {ep.Address}:{ep.Port} (filter universe={_ehubUniverseFilter})");
            }
            catch (Exception e)
            {
                Debug.LogError($"[EHubReceiver] Impossible de démarrer l'écoute: {e.Message}");
                StopListener();
            }
        }

        private void StopListener()
        {
            _running = false;
            try { _udp?.Close(); } catch { }
            _udp = null;

            if (_thread != null)
            {
                try { _thread.Join(250); } catch { }
                _thread = null;
            }
        }

        private void ReceiveLoop()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);

            while (_running)
            {
                try
                {
                    byte[] packet = _udp.Receive(ref remote);
                    if (packet == null || packet.Length < 10) continue;

                    if (packet[0] != (byte)'e' || packet[1] != (byte)'H' || packet[2] != (byte)'u' || packet[3] != (byte)'B')
                        continue;

                    byte type = packet[4];
                    byte universe = packet[5];
                    if (_ehubUniverseFilter != 0 && universe != (byte)_ehubUniverseFilter) continue;

                    ushort count = ReadU16(packet, 6);
                    ushort gzSize = ReadU16(packet, 8);
                    int payloadOffset = 10;
                    if (payloadOffset + gzSize > packet.Length) continue;

                    byte[] decompressed = DecompressGzip(packet, payloadOffset, gzSize);
                    if (decompressed == null || decompressed.Length == 0) continue;

                    if (type == 1)
                        ParseConfig(decompressed);
                    else if (type == 2)
                        ParseUpdate(decompressed, count);
                }
                catch (SocketException)
                {
                    // socket fermé lors d'un reload
                }
                catch (Exception)
                {
                    // silencieux pour ne pas spammer; on pourra rajouter un compteur d'erreurs si besoin
                }
            }
        }

        private void ParseConfig(byte[] payload)
        {
            // Chaque range = 4 ushorts (8 octets)
            if (payload.Length < 8) return;

            lock (_lock)
            {
                _ranges.Clear();
                int n = payload.Length / 8;
                for (int i = 0; i < n; i++)
                {
                    int o = i * 8;
                    ushort startTuple = ReadU16(payload, o + 0);
                    ushort startId = ReadU16(payload, o + 2);
                    ushort endTuple = ReadU16(payload, o + 4);
                    ushort endId = ReadU16(payload, o + 6);

                    _ranges.Add(new RangeMap
                    {
                        startTuple = startTuple,
                        startId = startId,
                        endTuple = endTuple,
                        endId = endId
                    });
                }
            }
        }

        private void ParseUpdate(byte[] payload, ushort declaredCount)
        {
            // Deux formats possibles rencontrés en pratique:
            // A) Sextuors 6 bytes: [id:ushort][r][g][b][w]
            // B) Quad 4 bytes: [r][g][b][w] avec IDs reconstruits via config ranges
            bool hasIds = payload.Length >= 6 && (payload.Length % 6 == 0);
            bool noIds = payload.Length >= 4 && (payload.Length % 4 == 0);

            EntityColor[] entities;

            if (hasIds)
            {
                int n = payload.Length / 6;
                entities = new EntityColor[n];
                for (int i = 0; i < n; i++)
                {
                    int o = i * 6;
                    ushort id = ReadU16(payload, o);
                    entities[i] = new EntityColor
                    {
                        id = id,
                        color = new Color32(payload[o + 2], payload[o + 3], payload[o + 4], 255)
                    };
                }
            }
            else if (noIds)
            {
                int n = payload.Length / 4;
                entities = new EntityColor[n];

                // Si pas de config, on ne sait pas mapper proprement les IDs
                List<RangeMap> rangesSnapshot;
                lock (_lock)
                    rangesSnapshot = new List<RangeMap>(_ranges);

                if (rangesSnapshot.Count == 0)
                    return;

                for (int tuple = 0; tuple < n; tuple++)
                {
                    int id = ResolveEntityId(tuple, rangesSnapshot);
                    if (id < 0) continue;

                    int o = tuple * 4;
                    entities[tuple] = new EntityColor
                    {
                        id = id,
                        color = new Color32(payload[o + 0], payload[o + 1], payload[o + 2], 255)
                    };
                }
            }
            else
            {
                return;
            }

            // Optionnel: utiliser declaredCount si différent; on garde payload comme source de vérité.
            lock (_lock)
                _latestEntities = entities;

            Interlocked.Increment(ref _updatesReceived);
            Interlocked.Exchange(ref _lastEntityCount, entities.Length);
        }

        private static int ResolveEntityId(int tupleIndex, List<RangeMap> ranges)
        {
            for (int i = 0; i < ranges.Count; i++)
            {
                var r = ranges[i];
                if (tupleIndex < r.startTuple || tupleIndex > r.endTuple) continue;

                int offset = tupleIndex - r.startTuple;
                return r.startId + offset;
            }
            return -1;
        }

        private static ushort ReadU16(byte[] buf, int offset)
        {
            // little-endian (convention .NET / la plupart des implémentations cours)
            return (ushort)(buf[offset] | (buf[offset + 1] << 8));
        }

        private static byte[] DecompressGzip(byte[] packet, int offset, int length)
        {
            try
            {
                using var input = new MemoryStream(packet, offset, length, writable: false);
                using var gz = new GZipStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                gz.CopyTo(output);
                return output.ToArray();
            }
            catch
            {
                return null;
            }
        }

        private struct RangeMap
        {
            public int startTuple;
            public int startId;
            public int endTuple;
            public int endId;
        }
    }
}

