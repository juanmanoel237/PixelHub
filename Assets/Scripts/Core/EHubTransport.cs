using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Laps.Core
{
    public enum EHubRole
    {
        Solo,
        Host,
        Client
    }

    public enum EHubClientLinkState
    {
        None,
        Connecting,
        Linked,
        Failed
    }

    /// <summary>
    /// Transport UDP eHub : mode Hôte (les autres se connectent à vous)
    /// ou mode Client (vous saisissez l'IP de l'hôte).
    /// </summary>
    public class EHubTransport : IDisposable
    {
        private readonly int _port;
        private readonly string _sessionId;
        private readonly string _clientId;
        private readonly string _localIp;

        private EHubRole _role = EHubRole.Solo;
        private EHubClientLinkState _clientLinkState = EHubClientLinkState.None;
        private string _hostIp;
        private string _lastConnectionError = "";
        private long _connectStartedMs;
        private readonly ConcurrentDictionary<string, long> _connectedClients = new ConcurrentDictionary<string, long>();

        private UdpClient _listener;
        private UdpClient _sender;
        private Thread _thread;
        private volatile bool _running;
        private readonly ConcurrentQueue<EHubMessage> _incoming = new ConcurrentQueue<EHubMessage>();
        private readonly EHubPeerTracker _peers = new EHubPeerTracker();
        private long _lastHostContactMs;

        public EHubRole Role => _role;
        public EHubClientLinkState ClientLinkState => _clientLinkState;
        public string ClientId => _clientId;
        public string SessionId => _sessionId;
        public string LocalIp => _localIp;
        public string HostIp => _hostIp ?? "";
        public string LastConnectionError => _lastConnectionError;
        public int Port => _port;
        public bool IsConnected => _role == EHubRole.Host || (_role == EHubRole.Client && IsClientLinked());
        public bool IsClientConnecting => _role == EHubRole.Client && _clientLinkState == EHubClientLinkState.Connecting;
        public bool IsListening => _running;
        public bool IsSoloMode => _role == EHubRole.Solo;
        public int MessagesReceived { get; private set; }
        public int MessagesSent { get; private set; }

        public int ConnectedClientCount => CountActiveClients();
        public int TotalPostes => _role == EHubRole.Host ? 1 + ConnectedClientCount : (_role == EHubRole.Client && IsClientLinked() ? 2 : 1);
        public string ActivePeersLabel => _role == EHubRole.Host ? FormatClientList() : (_role == EHubRole.Client ? $"hôte {_hostIp}" : "—");
        public string PeersConfigLabel => _role == EHubRole.Host ? $"{ConnectedClientCount} client(s)" : HostIp;

        public event Action<string> ClientJoined;
        public event Action ClientLinked;
        public event Action<string> ClientLinkFailed;
        public event Action<string> HostDiscovered;
        public event Action<string> HostConflictDetected;

        public string DiscoveredHostIp { get; private set; }
        public string LastHostConflictIp { get; private set; }
        private long _lastHostConflictLogMs;
        private bool _requestStateSyncOnNextHello;

        public EHubTransport(int port, string sessionId)
        {
            _port = port > 0 ? port : 9000;
            _sessionId = string.IsNullOrWhiteSpace(sessionId) ? "default" : sessionId.Trim();
            _clientId = Guid.NewGuid().ToString("N").Substring(0, 8);
            _localIp = EHubNetworkUtil.ResolveBestLocalIpv4();
        }

        public void StartAsHost()
        {
            _role = EHubRole.Host;
            _hostIp = null;
            _clientLinkState = EHubClientLinkState.None;
            _lastConnectionError = "";
            EnsureListener();
            Debug.Log($"[eHub] Mode HÔTE — session « {_sessionId} » port {_port}. Votre IP : {_localIp}");
        }

        public void ConnectToHost(string hostIp)
        {
            if (string.IsNullOrWhiteSpace(hostIp)) return;

            hostIp = EHubNetworkUtil.NormalizeIp(hostIp.Trim());
            if (EHubNetworkUtil.IsLoopbackOrSelf(_localIp, hostIp))
            {
                _lastConnectionError = "Impossible de se connecter à votre propre IP.";
                _clientLinkState = EHubClientLinkState.Failed;
                Debug.LogWarning($"[eHub] {_lastConnectionError}");
                return;
            }

            _role = EHubRole.Client;
            _hostIp = hostIp;
            _lastHostContactMs = 0;
            _clientLinkState = EHubClientLinkState.Connecting;
            _lastConnectionError = "";
            _connectStartedMs = NowMs();
            EnsureListener();
            if (!_running)
            {
                _clientLinkState = EHubClientLinkState.Failed;
                if (string.IsNullOrEmpty(_lastConnectionError))
                    _lastConnectionError = $"Impossible d'ouvrir le port UDP {_port} sur ce PC.";
                return;
            }
            SendHello();
            Debug.Log($"[eHub] Connexion à l'hôte {_hostIp}:{_port}…");
        }

        public void StartDiscoveryListen()
        {
            if (_role != EHubRole.Solo || _running) return;
            EnsureListener();
        }

        private void EnsureListener()
        {
            if (_running) return;
            StartListener();
        }

        public void Tick()
        {
            if (_role != EHubRole.Client) return;
            if (_clientLinkState == EHubClientLinkState.Linked) return;
            if (_clientLinkState != EHubClientLinkState.Connecting &&
                _clientLinkState != EHubClientLinkState.Failed) return;
            if (NowMs() - _connectStartedMs < 12000) return;

            _clientLinkState = EHubClientLinkState.Failed;
            _lastConnectionError =
                $"Pas de réponse de l'hôte {_hostIp}. L'hôte doit aussi autoriser Unity " +
                $"(pare-feu UDP {_port}). Même Wi-Fi requis.";
            ClientLinkFailed?.Invoke(_lastConnectionError);
            Debug.LogWarning($"[eHub] {_lastConnectionError}");
        }

        private void StartListener()
        {
            if (_running) return;

            try
            {
                var endpoint = new IPEndPoint(IPAddress.Any, _port);
                _listener = new UdpClient();
                _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Client.Bind(endpoint);
                _listener.Client.ReceiveBufferSize = 65536;
                _listener.EnableBroadcast = true;
            }
            catch (SocketException e)
            {
                _lastConnectionError = $"Port {_port} déjà utilisé sur ce PC ({e.Message}).";
                Debug.LogError($"[eHub] {_lastConnectionError}");
                return;
            }

            if (_sender == null)
            {
                _sender = new UdpClient();
                _sender.EnableBroadcast = true;
            }

            _running = true;
            if (_thread == null || !_thread.IsAlive)
            {
                _thread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = "PixelHub-eHub"
                };
                _thread.Start();
            }
        }

        private void ReceiveLoop()
        {
            while (_running)
            {
                try
                {
                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _listener.Receive(ref remote);
                    if (data == null || data.Length == 0) continue;

                    string remoteIp = EHubNetworkUtil.NormalizeIp(remote.Address.ToString());
                    if (!IsReceiveAllowed(remoteIp)) continue;

                    string json = Encoding.UTF8.GetString(data);
                    var msg = JsonUtility.FromJson<EHubMessage>(json);
                    if (msg == null || string.IsNullOrEmpty(msg.type)) continue;
                    if (msg.senderId == _clientId) continue;

                    if (msg.type == EHubMessageTypes.Hello)
                    {
                        if (!SessionMatches(msg)) continue;
                        HandleHello(remoteIp, msg);
                        continue;
                    }

                    if (msg.type == EHubMessageTypes.HostBeacon)
                    {
                        if (!SessionMatches(msg)) continue;
                        HandleHostBeacon(remoteIp, msg);
                        continue;
                    }

                    if (msg.type == EHubMessageTypes.HelloAck)
                    {
                        if (!SessionMatches(msg)) continue;
                        HandleHelloAck(remoteIp);
                        continue;
                    }

                    if (_role == EHubRole.Client &&
                        (_clientLinkState == EHubClientLinkState.Connecting ||
                         _clientLinkState == EHubClientLinkState.Failed) &&
                        IsFromTargetHost(remoteIp) &&
                        msg.type != EHubMessageTypes.HostBeacon)
                    {
                        HandleHelloAck(remoteIp);
                    }

                    if (!SessionMatches(msg)) continue;

                    _peers.Note(msg.senderId, remoteIp);
                    _lastHostContactMs = NowMs();

                    if (_role == EHubRole.Host)
                        RelayToClients(msg, remoteIp);

                    _incoming.Enqueue(msg);
                    MessagesReceived++;
                }
                catch (SocketException)
                {
                    if (!_running) break;
                }
                catch (Exception e)
                {
                    if (_running)
                        Debug.LogWarning($"[eHub] Erreur réception : {e.Message}");
                }
            }
        }

        private bool SessionMatches(EHubMessage msg)
        {
            if (string.IsNullOrEmpty(msg.sessionId)) return true;
            return string.Equals(msg.sessionId, _sessionId, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsFromTargetHost(string remoteIp)
        {
            if (EHubNetworkUtil.IpEquals(remoteIp, _hostIp)) return true;
            if (!string.IsNullOrEmpty(DiscoveredHostIp) &&
                EHubNetworkUtil.IpEquals(remoteIp, DiscoveredHostIp))
                return true;
            return false;
        }

        private void HandleHelloAck(string remoteIp)
        {
            if (_role != EHubRole.Client) return;

            bool wasLinked = _clientLinkState == EHubClientLinkState.Linked;
            _hostIp = remoteIp;
            _lastHostContactMs = NowMs();
            _clientLinkState = EHubClientLinkState.Linked;
            _lastConnectionError = "";

            if (!wasLinked)
            {
                Debug.Log($"[eHub] Connecté à l'hôte {_hostIp}");
                ClientLinked?.Invoke();
            }
        }

        private void HandleHostBeacon(string remoteIp, EHubMessage msg)
        {
            string hostIp = remoteIp;
            if (string.IsNullOrEmpty(hostIp)) return;

            if (_role == EHubRole.Host)
            {
                if (EHubNetworkUtil.IpEquals(hostIp, _localIp)) return;

                LastHostConflictIp = hostIp;
                long now = NowMs();
                if (now - _lastHostConflictLogMs > 5000)
                {
                    _lastHostConflictLogMs = now;
                    HostConflictDetected?.Invoke(hostIp);
                }
                return;
            }

            if (_role == EHubRole.Solo || (_role == EHubRole.Client && !IsClientLinked()))
            {
                if (EHubNetworkUtil.IsLoopbackOrSelf(_localIp, hostIp)) return;
                DiscoveredHostIp = hostIp;
                HostDiscovered?.Invoke(hostIp);
            }
        }

        public void SendHostBeacon()
        {
            if (_role != EHubRole.Host || _sender == null) return;

            var msg = new EHubMessage
            {
                type = EHubMessageTypes.HostBeacon,
                sessionId = _sessionId,
                senderId = _clientId,
                stringArg = _localIp
            };

            byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));

            try
            {
                _sender.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, _port));
                MessagesSent++;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[eHub] Beacon broadcast échoué : {e.Message}");
            }

            foreach (string ip in EHubNetworkUtil.CollectLanCandidates())
            {
                if (EHubNetworkUtil.IpEquals(ip, _localIp)) continue;
                try
                {
                    string subnet = ip.Substring(0, ip.LastIndexOf('.') + 1) + "255";
                    _sender.Send(data, data.Length, new IPEndPoint(IPAddress.Parse(subnet), _port));
                }
                catch { /* ignore */ }
            }
        }

        private void HandleHello(string remoteIp, EHubMessage msg)
        {
            if (_role != EHubRole.Host) return;

            string clientIp = remoteIp;
            bool isNewClient = !_connectedClients.ContainsKey(clientIp) ||
                               NowMs() - _connectedClients[clientIp] >= 8000;
            _connectedClients[clientIp] = NowMs();
            _peers.Note(msg.senderId, clientIp);

            if (!string.IsNullOrEmpty(msg.stringArg) &&
                !EHubNetworkUtil.IpEquals(msg.stringArg, remoteIp))
            {
                Debug.Log($"[eHub] Client {remoteIp} (déclaré {msg.stringArg})");
            }

            SendHelloAckTo(clientIp, 1 + CountActiveClients());

            bool wantsResync = !string.IsNullOrEmpty(msg.stringArg) &&
                               msg.stringArg.IndexOf("|resync", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isNewClient || wantsResync)
                ClientJoined?.Invoke(clientIp);

            if (isNewClient)
                Debug.Log($"[eHub] Nouveau client : {clientIp}");
        }

        private void SendHelloAckTo(string clientIp, int posteCount)
        {
            var ack = new EHubMessage
            {
                type = EHubMessageTypes.HelloAck,
                intArg = posteCount
            };

            for (int i = 0; i < 3; i++)
                SendToIp(ack, clientIp);
        }

        private void RelayToClients(EHubMessage msg, string exceptIp)
        {
            byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
            exceptIp = EHubNetworkUtil.NormalizeIp(exceptIp);

            foreach (string clientIp in GetActiveClientIps())
            {
                if (EHubNetworkUtil.IpEquals(clientIp, exceptIp)) continue;
                try
                {
                    _sender.Send(data, data.Length, new IPEndPoint(IPAddress.Parse(clientIp), _port));
                    MessagesSent++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[eHub] Relais vers {clientIp} échoué : {e.Message}");
                }
            }
        }

        private bool IsReceiveAllowed(string remoteIp)
        {
            remoteIp = EHubNetworkUtil.NormalizeIp(remoteIp);
            if (_role == EHubRole.Host) return true;
            if (_role == EHubRole.Solo) return true;

            if (_role == EHubRole.Client)
            {
                if (_clientLinkState == EHubClientLinkState.Connecting ||
                    _clientLinkState == EHubClientLinkState.Failed)
                    return true;

                return EHubNetworkUtil.IpEquals(remoteIp, _hostIp);
            }

            return false;
        }

        private bool IsClientLinked() =>
            _clientLinkState == EHubClientLinkState.Linked &&
            !string.IsNullOrEmpty(_hostIp) &&
            NowMs() - _lastHostContactMs < 12000;

        public bool TryDequeue(out EHubMessage msg) => _incoming.TryDequeue(out msg);

        public void RequestStateSyncOnNextHello() => _requestStateSyncOnNextHello = true;

        public void SendHello()
        {
            if (_role != EHubRole.Client || string.IsNullOrEmpty(_hostIp)) return;

            string helloArg = _localIp;
            if (_requestStateSyncOnNextHello)
            {
                helloArg += "|resync";
                _requestStateSyncOnNextHello = false;
            }

            var msg = new EHubMessage
            {
                type = EHubMessageTypes.Hello,
                stringArg = helloArg
            };

            // Unicast direct + broadcast (certains Wi-Fi bloquent le trafic direct entre clients)
            SendToIp(msg, _hostIp);
            BroadcastPacket(msg);
        }

        private void BroadcastPacket(EHubMessage msg)
        {
            if (_sender == null) return;

            msg.sessionId = _sessionId;
            msg.senderId = _clientId;
            byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));

            try
            {
                _sender.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, _port));
            }
            catch { /* ignore */ }

            foreach (string ip in EHubNetworkUtil.CollectLanCandidates())
            {
                try
                {
                    int dot = ip.LastIndexOf('.');
                    if (dot < 0) continue;
                    string subnet = ip.Substring(0, dot + 1) + "255";
                    _sender.Send(data, data.Length, new IPEndPoint(IPAddress.Parse(subnet), _port));
                }
                catch { /* ignore */ }
            }
        }

        public void SendToPeer(EHubMessage msg, string ip)
        {
            if (msg == null || _sender == null || string.IsNullOrWhiteSpace(ip)) return;
            SendToIp(msg, EHubNetworkUtil.NormalizeIp(ip.Trim()));
        }

        public void Send(EHubMessage msg)
        {
            if (msg == null || _sender == null || _role == EHubRole.Solo) return;

            msg.sessionId = _sessionId;
            msg.senderId = _clientId;

            if (_role == EHubRole.Host)
            {
                foreach (string ip in GetActiveClientIps())
                    SendToIp(msg, ip);
            }
            else if (_role == EHubRole.Client && !string.IsNullOrEmpty(_hostIp))
            {
                SendToIp(msg, _hostIp);
            }
        }

        private void SendToIp(EHubMessage msg, string ip)
        {
            if (string.IsNullOrEmpty(ip)) return;

            msg.sessionId = _sessionId;
            msg.senderId = _clientId;
            byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));

            try
            {
                _sender.Send(data, data.Length, new IPEndPoint(IPAddress.Parse(ip), _port));
                MessagesSent++;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[eHub] Envoi vers {ip}:{_port} échoué : {e.Message}");
            }
        }

        private int CountActiveClients()
        {
            long now = NowMs();
            int count = 0;
            foreach (var kv in _connectedClients)
                if (now - kv.Value < 12000) count++;
            return count;
        }

        private IEnumerable<string> GetActiveClientIps()
        {
            long now = NowMs();
            foreach (var kv in _connectedClients)
                if (now - kv.Value < 12000)
                    yield return kv.Key;
        }

        private string FormatClientList()
        {
            long now = NowMs();
            var ips = new List<string>();
            foreach (var kv in _connectedClients)
                if (now - kv.Value < 12000)
                    ips.Add(kv.Key);
            return ips.Count == 0 ? "en attente…" : string.Join(", ", ips);
        }

        private static long NowMs() => DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;

        public void Dispose()
        {
            _running = false;
            _role = EHubRole.Solo;
            _clientLinkState = EHubClientLinkState.None;
            try { _listener?.Close(); } catch { /* ignore */ }
            try { _sender?.Close(); } catch { /* ignore */ }
            _listener = null;
            _sender = null;
            _thread?.Join(300);
            _thread = null;
        }
    }
}
