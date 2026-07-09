const dgram = require("dgram");

// ---------------------------------------------------------------------------
// Script "simple comme le prof"
// - Envoie 1 trame Art-Net (ArtDMX)
// - Met la première LED (canaux DMX 1-3) en rouge/vert/bleu
// Utilisation:
//   node send-artnet.js red
//   node send-artnet.js green
//   node send-artnet.js blue
// ---------------------------------------------------------------------------

const TARGET_IP = "192.168.1.45";
const ARTNET_PORT = 6454;
const UNIVERSE = 1; // aligné avec config.json (startUniverse: 1)
const DMX_CHANNELS = 512;

// On prépare 512 canaux DMX, initialisés à 0
const dmxData = Buffer.alloc(DMX_CHANNELS, 0);

// Couleur choisie en argument
const color = (process.argv[2] || '').toLowerCase();

// Première LED = canaux 0-2 (R, G, B) — DMX index 0-based dans le Buffer
dmxData[0] = color === "red"   ? 255 : 0;
dmxData[1] = color === "green" ? 255 : 0;
dmxData[2] = color === "blue"  ? 255 : 0;

function buildArtDmxPacket(universe, dmxData, sequence = 0) {
  const header = Buffer.alloc(18);

  header.write("Art-Net\0", 0, "ascii"); // ID (8 octets, terminé par 0)
  header.writeUInt16LE(0x5000, 8); // OpCode ArtDmx (little-endian)
  header.writeUInt16BE(14, 10); // ProtVer (big-endian)
  header.writeUInt8(sequence & 0xff, 12); // Sequence
  header.writeUInt8(0, 13); // Physical
  header.writeUInt16LE(universe & 0xffff, 14); // Univers (little-endian)
  header.writeUInt16BE(dmxData.length & 0xffff, 16); // Longueur (big-endian)

  return Buffer.concat([header, dmxData]);
}

const socket = dgram.createSocket("udp4");
const packet = buildArtDmxPacket(UNIVERSE, dmxData, 0);

socket.send(packet, ARTNET_PORT, TARGET_IP, (err) => {
  if (err) {
    console.error(err);
    process.exitCode = 1;
  } else {
    console.log(
      `Sent ArtDMX to ${TARGET_IP}:${ARTNET_PORT} (universe ${UNIVERSE}) - channel 1 = ${color}`
    );
  }
  socket.close();
}); 