const dgram = require("dgram");

// Scan tous les univers de 0 à 15 en envoyant du rouge
// pour trouver lequel allume la LED

const TARGET_IP = "192.168.1.45";
const ARTNET_PORT = 6454;
const DMX_CHANNELS = 512;

function buildArtDmxPacket(universe, dmxData, sequence = 0) {
  const header = Buffer.alloc(18);
  header.write("Art-Net\0", 0, "ascii");
  header.writeUInt16LE(0x5000, 8);
  header.writeUInt16BE(14, 10);
  header.writeUInt8(sequence & 0xff, 12);
  header.writeUInt8(0, 13);
  header.writeUInt16LE(universe & 0xffff, 14);
  header.writeUInt16BE(dmxData.length & 0xffff, 16);
  return Buffer.concat([header, dmxData]);
}

async function sendUniverse(universe) {
  return new Promise((resolve) => {
    const dmxData = Buffer.alloc(DMX_CHANNELS, 0);
    dmxData[0] = 255; // R
    dmxData[1] = 0;   // G
    dmxData[2] = 0;   // B

    const socket = dgram.createSocket("udp4");
    const packet = buildArtDmxPacket(universe, dmxData, 0);

    socket.send(packet, ARTNET_PORT, TARGET_IP, (err) => {
      if (err) console.error(`  Universe ${universe}: ERROR`, err.message);
      else console.log(`  ➜ Universe ${universe.toString().padStart(3)} (0x${universe.toString(16).padStart(2,"0")}) envoyé`);
      socket.close();
      resolve();
    });
  });
}

async function main() {
  const targetUniverses = [0, 1, 2, 3, 4, 14, 15, 16, 30, 31];

  console.log(`Envoi de rouge sur plusieurs univers vers ${TARGET_IP}:${ARTNET_PORT}`);
  console.log("Regarde quelle LED s'allume !\n");

  for (const u of targetUniverses) {
    await sendUniverse(u);
    await new Promise(r => setTimeout(r, 500)); // 500ms entre chaque
  }

  console.log("\nDone. Note l'univers qui a allumé la LED.");
}

main();
