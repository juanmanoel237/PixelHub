using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace Laps.Routing
{
    /// <summary>
    /// Gère l'envoi de paquets ArtNet (DMX over UDP) vers les contrôleurs LED.
    ///
    /// Protocole ArtNet (https://art-net.org.uk) :
    ///   - Port UDP : 6454 (0x1936)
    ///   - Header  : "Art-Net\0" (8 octets)
    ///   - OpCode  : 0x5000 (ArtDmx, little-endian = 0x00 0x50)
    ///   - ProtVer : 0x00 0x0E (version 14)
    ///   - Sequence: 1 octet (0 = désactivé)
    ///   - Physical: 1 octet (0)
    ///   - SubUni  : 1 octet (univers[0:7])
    ///   - Net     : 1 octet (univers[8:14])
    ///   - Length  : 2 octets big-endian (multiple de 2, max 512)
    ///   - Data    : jusqu'à 512 octets DMX
    ///
    /// Satisfait P2 : Routage ArtNet vers les contrôleurs.
    /// </summary>
    public class ArtNetSender : IDisposable
    {
        // ── Constantes ArtNet ──────────────────────────────────
        private static readonly byte[] ArtNetHeader = new byte[]
        {
            0x41, 0x72, 0x74, 0x2D, 0x4E, 0x65, 0x74, 0x00 // "Art-Net\0"
        };
        private const byte OPCODE_LO  = 0x00; // OpCode ArtDMX (little-endian)
        private const byte OPCODE_HI  = 0x50;
        private const byte PROTVER_HI = 0x00; // Protocol version 14
        private const byte PROTVER_LO = 0x0E;
        private const int  ARTNET_PORT = 6454;
        private const int  DMX_CHANNELS = 512;
        private const int  HEADER_SIZE = 18; // Taille du header ArtNet complet

        // ── Etat interne ───────────────────────────────────────
        private UdpClient _udpClient;
        private byte _sequence = 1; // Compteur séquence (1-255, 0 = désactivé)

        // Buffer réutilisable pour éviter les allocations à chaque paquet
        private readonly byte[] _packetBuffer = new byte[HEADER_SIZE + DMX_CHANNELS];

        // Stats de débogage (P8)
        public int PacketsSent { get; private set; }
        public float PacketsPerSecond { get; private set; }
        private int _packetsThisSecond;
        private float _statsTimer;

        // ── Initialisation ─────────────────────────────────────

        public ArtNetSender()
        {
            _udpClient = new UdpClient();
            _udpClient.Client.SendBufferSize = 65536;

            // Pré-remplir le header statique du buffer
            Array.Copy(ArtNetHeader, 0, _packetBuffer, 0, 8);
            _packetBuffer[8]  = OPCODE_LO;
            _packetBuffer[9]  = OPCODE_HI;
            _packetBuffer[10] = PROTVER_HI;
            _packetBuffer[11] = PROTVER_LO;
        }

        // ── API publique ────────────────────────────────────────

        /// <summary>
        /// Démarre une frame DMX : tous les univers envoyés ensuite partagent le même
        /// numéro de séquence ArtNet (évite le clignotement sur multi-univers).
        /// </summary>
        public void BeginFrame()
        {
            // 0 = séquence désactivée (recommandé Art-Net pour multi-univers synchronisés)
            _sequence = 0;
        }

        /// <summary>
        /// Envoie un univers DMX complet vers une adresse IP.
        /// </summary>
        /// <param name="ip">Adresse IP du contrôleur</param>
        /// <param name="universe">Univers DMX absolu (0-32767)</param>
        /// <param name="dmxData">Tableau de 512 octets DMX</param>
        /// <param name="dataLength">Nombre de canaux à envoyer (arrondi au multiple de 2)</param>
        public void SendUniverse(string ip, int universe, byte[] dmxData, int dataLength = DMX_CHANNELS)
        {
            if (_udpClient == null) return;

            // Longueur doit être paire et ≤ 512
            dataLength = Mathf.Min(dataLength, DMX_CHANNELS);
            if (dataLength % 2 != 0) dataLength++;

            // ── Remplir le header ─────────────────────────────
            _packetBuffer[12] = _sequence;           // Sequence
            _packetBuffer[13] = 0;                   // Physical
            _packetBuffer[14] = (byte)(universe & 0xFF);        // SubUni (bits 0-7)
            _packetBuffer[15] = (byte)((universe >> 8) & 0x7F); // Net (bits 8-14)
            _packetBuffer[16] = (byte)(dataLength >> 8);        // Length HI (big-endian)
            _packetBuffer[17] = (byte)(dataLength & 0xFF);      // Length LO

            // ── Copier les données DMX ────────────────────────
            Array.Copy(dmxData, 0, _packetBuffer, HEADER_SIZE, dataLength);

            // ── Envoyer ──────────────────────────────────────
            try
            {
                var endpoint = new IPEndPoint(IPAddress.Parse(ip), ARTNET_PORT);
                int totalSize = HEADER_SIZE + dataLength;
                _udpClient.Send(_packetBuffer, totalSize, endpoint);

                PacketsSent++;
                _packetsThisSecond++;
            }
            catch (SocketException e)
            {
                Debug.LogWarning($"[ArtNetSender] Erreur réseau vers {ip}:{universe} — {e.Message}");
            }
        }

        /// <summary>
        /// Met à jour les statistiques de paquets/seconde. À appeler depuis le thread de routage.
        /// </summary>
        public void UpdateStats(float deltaTime)
        {
            _statsTimer += deltaTime;
            if (_statsTimer >= 1f)
            {
                PacketsPerSecond = _packetsThisSecond / _statsTimer;
                _packetsThisSecond = 0;
                _statsTimer = 0f;
            }
        }

        // ── Nettoyage ───────────────────────────────────────────

        public void Dispose()
        {
            _udpClient?.Close();
            _udpClient = null;
        }
    }
}
