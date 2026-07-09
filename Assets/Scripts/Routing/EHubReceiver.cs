using System;
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
    /// Écoute le réseau sur le port UDP eHuB (9000 par défaut)
    /// Décompresse les paquets envoyés par le logiciel de conception "Tan"
    /// et extrait les couleurs de chaque Entité.
    /// Implémente IStateProvider pour router directement cet état vers les BC216.
    /// </summary>
    public class EHubReceiver : MonoBehaviour, IStateProvider
    {
        public bool IsEntityBased => true;

        private Color32[] _entityState = new Color32[20000];
        private LyreState[] _emptyLyres = new LyreState[0];

        private UdpClient _udpClient;
        private Thread _receiveThread;
        private bool _running;

        public Color32[] GetState() => _entityState;
        public LyreState[] GetLyreStates() => _emptyLyres;

        private void OnEnable()
        {
            StartReceiver();
        }

        private void OnDisable()
        {
            StopReceiver();
        }

        private void StartReceiver()
        {
            if (_running) return;
            
            var config = ConfigManager.Config;
            int port = config?.network?.eHubPort ?? 9000;

            try
            {
                _udpClient = new UdpClient(port);
                _running = true;
                _receiveThread = new Thread(ReceiveLoop)
                {
                    Name = "EHub-Receiver",
                    IsBackground = true
                };
                _receiveThread.Start();
                Debug.Log($"[EHubReceiver] Écoute UDP eHuB démarrée sur le port {port}.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[EHubReceiver] Erreur au démarrage UDP sur le port {port}: {e.Message}");
            }
        }

        private void StopReceiver()
        {
            _running = false;
            if (_udpClient != null)
            {
                _udpClient.Close();
                _udpClient = null;
            }
            if (_receiveThread != null && _receiveThread.IsAlive)
            {
                _receiveThread.Join(500);
            }
            Debug.Log("[EHubReceiver] Écoute UDP eHuB arrêtée.");
        }

        private void ReceiveLoop()
        {
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);

            while (_running)
            {
                try
                {
                    byte[] data = _udpClient.Receive(ref endPoint);
                    ProcessPacket(data);
                }
                catch (SocketException)
                {
                    // Peut arriver lors du Close()
                    break;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EHubReceiver] Exception lors de la réception : {e.Message}");
                }
            }
        }

        private void ProcessPacket(byte[] packet)
        {
            // Vérifier la taille minimale
            if (packet == null || packet.Length < 10) return;

            // Header "eHuB"
            if (packet[0] != 'e' || packet[1] != 'H' || packet[2] != 'u' || packet[3] != 'B') return;

            byte type = packet[4];
            
            if (type == 2) // Update
            {
                // Format:
                // 5: universe (1 byte)
                // 6-7: entityCount (2 bytes)
                // 8-9: compressedPayloadSize (2 bytes)
                // 10+: gzip payload
                
                int entityCount = BitConverter.ToUInt16(packet, 6);
                int compressedSize = BitConverter.ToUInt16(packet, 8);

                if (packet.Length < 10 + compressedSize) return;

                using (var ms = new MemoryStream(packet, 10, packet.Length - 10))
                using (var gzip = new GZipStream(ms, CompressionMode.Decompress))
                using (var outMs = new MemoryStream())
                {
                    gzip.CopyTo(outMs);
                    byte[] payload = outMs.ToArray();

                    // Vérifier que la taille du payload correspond au nombre d'entités (6 octets par entité)
                    if (payload.Length >= entityCount * 6)
                    {
                        for (int i = 0; i < entityCount; i++)
                        {
                            int offset = i * 6;
                            int entityId = BitConverter.ToUInt16(payload, offset);
                            byte r = payload[offset + 2];
                            byte g = payload[offset + 3];
                            byte b = payload[offset + 4];
                            // byte w = payload[offset + 5]; // Ignoré pour l'instant (RGB 3 canaux)

                            if (entityId < _entityState.Length)
                            {
                                _entityState[entityId] = new Color32(r, g, b, 255);
                            }
                        }
                    }
                }
            }
            else if (type == 1) // Config
            {
                // On ignore le message config car le message update 
                // transporte déjà l'ID de l'entité pour chaque couleur.
            }
        }
    }
}
