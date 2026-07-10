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
        private string _hostIp;
        private readonly ConcurrentDictionary<string, long> _connectedClients = new ConcurrentDictionary<string, long>();

        private UdpClient _listener;
        private UdpClient _sender;
        private Thread _thread;
        private volatile bool _running;
        private readonly ConcurrentQueue<EHubMessage> _incoming = new ConcurrentQueue<EHubMessage>();
        private readonly EHubPeerTracker _peers = new EHubPeerTracker();
        private long _lastHostContactMs;

        public EHubRole Role => _role;
        public string ClientId => _clientId;
        public string SessionId => _sessionId;
        public string LocalIp => _localIp;
        public string HostIp => _hostIp ?? "";
        public int Port => _port;
        public bool IsConnected => _role == EHubRole.Host || (_role == EHubRole.Client && IsClientLinked());
        public bool IsSoloMode => _role == EHubRole.Solo;
        public int MessagesReceived { get; private set; }
        public int MessagesSent { get; private set; }

        public int ConnectedClientCount => CountActiveClients();
        public int TotalPostes => _role == EHubRole.Host ? 1 + ConnectedClientCount : (_role == EHubRole.Client && IsClientLinked() ? 2 : 1);
        public string ActivePeersLabel => _role == EHubRole.Host ? FormatClientList() : (_role == EHubRole.Client ? $"hôte {_hostIp}" : "—");
        public string PeersConfigLabel => _role == EHubRole.Host ? $"{ConnectedClientCount} client(s)" : HostIp;

        public EHubTransport(int port, string sessionId)
        {
            _port = port > 0 ? port : 9000;
            _sessionId = string.IsNullOrWhiteSpace(sessionId) ? "default" : sessionId.Trim();
            _clientId = Guid.NewGuid().ToString("N").Substring(0, 8);
            _localIp = ResolveLocalIpv4();
        }

        public void StartAsHost()
        {
            _role = EHubRole.Host;
            _hostIp = null;
            StartListener();
            Debug.Log($"[eHub] Mode HÔTE — session « {_sessionId} » port {_port}. Votre IP : {_localIp}");
        }

        public void ConnectToHost(string hostIp)
        {
            if (string.IsNullOrWhiteSpace(hostIp)) return;

            _role = EHubRole.Client;
            _hostIp = hostIp.Trim();
            _lastHostContactMs = 0;
            StartListener();
            SendHello();
            Debug.Log($"[eHub] Connexion à l'hôte {_hostIp}:{_port}…");
        }

        private void StartListener()
        {
            if (_running) return;

            _listener = new UdpClient(_port);
            _listener.Client.ReceiveBufferSize = 65536;

            _sender = new UdpClient();
            _sender.EnableBroadcast = false;

            _running = true;
            _thread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "PixelHub-eHub"
            };
            _thread.Start();
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

                    string remoteIp = remote.Address.ToString();
                    if (!IsReceiveAllowed(remoteIp)) continue;

                    string json = Encoding.UTF8.GetString(data);
                    var msg = JsonUtility.FromJson<EHubMessage>(json);
                    if (msg == null || string.IsNullOrEmpty(msg.type)) continue;
                    if (msg.senderId == _clientId) continue;
                    if (!string.Equals(msg.sessionId, _sessionId, StringComparison.OrdinalIgnoreCase)) continue;

                    if (msg.type == EHubMessageTypes.Hello)
                    {
                        HandleHello(remoteIp, msg);
                        continue;
                    }

                    if (msg.type == EHubMessageTypes.HelloAck)
                    {
                        _lastHostContactMs = NowMs();
                        continue;
                    }

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

        private void HandleHello(string remoteIp, EHubMessage msg)
        {
            if (_role != EHubRole.Host) return;

            string clientIp = string.IsNullOrEmpty(msg.stringArg) ? remoteIp : msg.stringArg.Trim();
            _connectedClients[clientIp] = NowMs();
            _peers.Note(msg.senderId, clientIp);

            SendToIp(new EHubMessage
            {
                type = EHubMessageTypes.HelloAck,
                intArg = 1 + CountActiveClients()
            }, clientIp);
        }

        private void RelayToClients(EHubMessage msg, string exceptIp)
        {
            byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
            foreach (string clientIp in GetActiveClientIps())
            {
                if (string.Equals(clientIp, exceptIp, StringComparison.OrdinalIgnoreCase)) continue;
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
            if (_role == EHubRole.Host) return true;
            if (_role == EHubRole.Client)
                return string.Equals(remoteIp, _hostIp, StringComparison.OrdinalIgnoreCase);
            return false;
        }

        private bool IsClientLinked()
        {
            return !string.IsNullOrEmpty(_hostIp) &&
                   NowMs() - _lastHostContactMs < 8000;
        }

        public bool TryDequeue(out EHubMessage msg) => _incoming.TryDequeue(out msg);

        public void SendHello()
        {
            if (_role != EHubRole.Client || string.IsNullOrEmpty(_hostIp)) return;

            SendToIp(new EHubMessage
            {
                type = EHubMessageTypes.Hello,
                stringArg = _localIp
            }, _hostIp);
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
                if (now - kv.Value < 8000) count++;
            return count;
        }

        private IEnumerable<string> GetActiveClientIps()
        {
            long now = NowMs();
            foreach (var kv in _connectedClients)
                if (now - kv.Value < 8000)
                    yield return kv.Key;
        }

        private string FormatClientList()
        {
            long now = NowMs();
            var ips = new List<string>();
            foreach (var kv in _connectedClients)
                if (now - kv.Value < 8000)
                    ips.Add(kv.Key);
            return ips.Count == 0 ? "en attente…" : string.Join(", ", ips);
        }

        private static long NowMs() => DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;

        public void Dispose()
        {
            _running = false;
            _role = EHubRole.Solo;
            try { _listener?.Close(); } catch { /* ignore */ }
            try { _sender?.Close(); } catch { /* ignore */ }
            _listener = null;
            _sender = null;
            _thread?.Join(300);
            _thread = null;
        }

        private static string ResolveLocalIpv4()
        {
            try
            {
                foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        return ip.ToString();
                }
            }
            catch { /* ignore */ }

            return "127.0.0.1";
        }
    }
}
