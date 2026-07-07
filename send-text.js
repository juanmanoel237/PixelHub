const dgram = require("dgram");

// ---------------------------------------------------------------------------
// send-text.js — Affiche du texte sur une matrice LED via Art-Net
// Écran : 4 panneaux 128×128 = 256×256 pixels total
// Usage:
//   node send-text.js "space"              ← texte blanc, haut-gauche
//   node send-text.js "space" ff8800       ← couleur hex
//   node send-text.js --fill ff0000        ← remplit tout en rouge (test)
//   node send-text.js --fill-universe 1 ff0000
//
// Options d'env :
//   MATRIX_W=256 MATRIX_H=256 SCALE=5 START_UNIVERSE=0 SERPENTINE=1
// ---------------------------------------------------------------------------

const TARGET_IP      = "192.168.1.45";
const ARTNET_PORT    = 6454;
const MATRIX_W       = parseInt(process.env.MATRIX_W)  || 256;  // 4×128 = 256
const MATRIX_H       = parseInt(process.env.MATRIX_H)  || 256;  // 4×128 = 256
const CH_PER_LED     = 3;
const LEDS_PER_UNIV  = Math.floor(512 / CH_PER_LED); // 170
const START_UNIVERSE = process.env.START_UNIVERSE !== undefined ? parseInt(process.env.START_UNIVERSE) : 0;
const SCALE          = parseInt(process.env.SCALE) || 5; // ×5 = lettres 25×35px
const SERPENTINE     = process.env.SERPENTINE !== '0'; // true par défaut (matrices zigzag)

// ---------------------------------------------------------------------------
// Font bitmap 5×7
// ---------------------------------------------------------------------------
const FONT = {
  ' ': [0b00000,0b00000,0b00000,0b00000,0b00000,0b00000,0b00000],
  'a': [0b01110,0b10001,0b10001,0b11111,0b10001,0b10001,0b10001],
  'b': [0b11110,0b10001,0b10001,0b11110,0b10001,0b10001,0b11110],
  'c': [0b01110,0b10001,0b10000,0b10000,0b10000,0b10001,0b01110],
  'd': [0b11100,0b10010,0b10001,0b10001,0b10001,0b10010,0b11100],
  'e': [0b11111,0b10000,0b10000,0b11110,0b10000,0b10000,0b11111],
  'f': [0b11111,0b10000,0b10000,0b11110,0b10000,0b10000,0b10000],
  'g': [0b01110,0b10001,0b10000,0b10111,0b10001,0b10001,0b01110],
  'h': [0b10001,0b10001,0b10001,0b11111,0b10001,0b10001,0b10001],
  'i': [0b11111,0b00100,0b00100,0b00100,0b00100,0b00100,0b11111],
  'j': [0b11111,0b00010,0b00010,0b00010,0b00010,0b10010,0b01100],
  'k': [0b10001,0b10010,0b10100,0b11000,0b10100,0b10010,0b10001],
  'l': [0b10000,0b10000,0b10000,0b10000,0b10000,0b10000,0b11111],
  'm': [0b10001,0b11011,0b10101,0b10101,0b10001,0b10001,0b10001],
  'n': [0b10001,0b11001,0b10101,0b10011,0b10001,0b10001,0b10001],
  'o': [0b01110,0b10001,0b10001,0b10001,0b10001,0b10001,0b01110],
  'p': [0b11110,0b10001,0b10001,0b11110,0b10000,0b10000,0b10000],
  'q': [0b01110,0b10001,0b10001,0b10001,0b10101,0b10010,0b01101],
  'r': [0b11110,0b10001,0b10001,0b11110,0b10100,0b10010,0b10001],
  's': [0b01111,0b10000,0b10000,0b01110,0b00001,0b00001,0b11110],
  't': [0b11111,0b00100,0b00100,0b00100,0b00100,0b00100,0b00100],
  'u': [0b10001,0b10001,0b10001,0b10001,0b10001,0b10001,0b01110],
  'v': [0b10001,0b10001,0b10001,0b10001,0b10001,0b01010,0b00100],
  'w': [0b10001,0b10001,0b10001,0b10101,0b10101,0b11011,0b10001],
  'x': [0b10001,0b01010,0b00100,0b00100,0b00100,0b01010,0b10001],
  'y': [0b10001,0b10001,0b01010,0b00100,0b00100,0b00100,0b00100],
  'z': [0b11111,0b00001,0b00010,0b00100,0b01000,0b10000,0b11111],
  '0': [0b01110,0b10001,0b10011,0b10101,0b11001,0b10001,0b01110],
  '1': [0b00100,0b01100,0b00100,0b00100,0b00100,0b00100,0b11111],
  '2': [0b01110,0b10001,0b00001,0b00110,0b01000,0b10000,0b11111],
  '3': [0b11111,0b00001,0b00010,0b00110,0b00001,0b10001,0b01110],
  '4': [0b00010,0b00110,0b01010,0b10010,0b11111,0b00010,0b00010],
  '5': [0b11111,0b10000,0b11110,0b00001,0b00001,0b10001,0b01110],
  '6': [0b01110,0b10000,0b10000,0b11110,0b10001,0b10001,0b01110],
  '7': [0b11111,0b00001,0b00010,0b00100,0b01000,0b01000,0b01000],
  '8': [0b01110,0b10001,0b10001,0b01110,0b10001,0b10001,0b01110],
  '9': [0b01110,0b10001,0b10001,0b01111,0b00001,0b00001,0b01110],
  '!': [0b00100,0b00100,0b00100,0b00100,0b00000,0b00000,0b00100],
  '.': [0b00000,0b00000,0b00000,0b00000,0b00000,0b00000,0b00100],
};

const CHAR_W   = 5;
const CHAR_H   = 7;
const CHAR_GAP = 1;

// ---------------------------------------------------------------------------
// Framebuffer
// ---------------------------------------------------------------------------
const fb = Buffer.alloc(MATRIX_W * MATRIX_H * CH_PER_LED, 0);

function setPixel(x, y, r, g, b) {
  if (x < 0 || x >= MATRIX_W || y < 0 || y >= MATRIX_H) return;
  // Serpentine : les lignes impaires sont câblées de droite à gauche
  const physX = (SERPENTINE && y % 2 === 1) ? (MATRIX_W - 1 - x) : x;
  const i = (y * MATRIX_W + physX) * CH_PER_LED;
  fb[i] = r; fb[i+1] = g; fb[i+2] = b;
}

// ---------------------------------------------------------------------------
// Parse args
// ---------------------------------------------------------------------------
const args = process.argv.slice(2);

// Mode --fill-universe N color
if (args[0] === '--fill-universe') {
  const targetUniv = parseInt(args[1]);
  const hex = args[2] || 'ffffff';
  const R = parseInt(hex.slice(0,2),16), G = parseInt(hex.slice(2,4),16), B = parseInt(hex.slice(4,6),16);
  console.log(`FILL univers ${targetUniv} → couleur #${hex.toUpperCase()}`);
  sendSingleUniverseFill(targetUniv, R, G, B);

// Mode --fill color
} else if (args[0] === '--fill') {
  const hex = args[1] || 'ffffff';
  const R = parseInt(hex.slice(0,2),16), G = parseInt(hex.slice(2,4),16), B = parseInt(hex.slice(4,6),16);
  fb.fill(0);
  for (let i = 0; i < MATRIX_W * MATRIX_H; i++) {
    fb[i*CH_PER_LED]=R; fb[i*CH_PER_LED+1]=G; fb[i*CH_PER_LED+2]=B;
  }
  const totalU = Math.ceil(MATRIX_W*MATRIX_H / LEDS_PER_UNIV);
  console.log(`FILL total : ${totalU} univers → #${hex.toUpperCase()}`);
  sendAllUniverses(totalU);

// Mode texte
} else {
  const text  = (args[0] || 'space').toLowerCase();
  const hex   = args[1] || 'ffffff';
  const R = parseInt(hex.slice(0,2),16), G = parseInt(hex.slice(2,4),16), B = parseInt(hex.slice(4,6),16);

  const glyphW = CHAR_W * SCALE;
  const glyphH = CHAR_H * SCALE;
  const gap    = CHAR_GAP * SCALE;

  const totalTextW = text.length * (glyphW + gap) - gap;
  // Positionné en HAUT-GAUCHE avec marge de 2px pour rester dans les premiers univers
  const startX = 2;
  const startY = 2;

  for (let ci = 0; ci < text.length; ci++) {
    const ch = text[ci];
    const glyph = FONT[ch] || FONT[' '];
    const ox = startX + ci * (glyphW + gap);

    for (let row = 0; row < CHAR_H; row++) {
      const bits = glyph[row];
      for (let col = 0; col < CHAR_W; col++) {
        if (bits & (1 << (CHAR_W - 1 - col))) {
          // Scale : dessine un bloc SCALE×SCALE par pixel de font
          for (let sy = 0; sy < SCALE; sy++)
            for (let sx = 0; sx < SCALE; sx++)
              setPixel(ox + col*SCALE + sx, startY + row*SCALE + sy, R, G, B);
        }
      }
    }
  }

  // Calcul des univers concernés
  const lastPixel   = (startY + glyphH) * MATRIX_W + (startX + totalTextW);
  const lastUniverse = Math.ceil(lastPixel / LEDS_PER_UNIV);
  const numUniverses = lastUniverse + 1;

  console.log(`Texte : "${text}"  Couleur : #${hex.toUpperCase()}  Échelle : ×${SCALE}`);
  console.log(`Position : (${startX},${startY})  Taille texte : ${totalTextW}×${glyphH}px`);
  console.log(`Univers concernés : ${START_UNIVERSE} → ${START_UNIVERSE + lastUniverse}`);
  sendAllUniverses(numUniverses);
}

// ---------------------------------------------------------------------------
// Envoi des paquets Art-Net
// ---------------------------------------------------------------------------
function buildArtDmxPacket(universe, dmxSlice, sequence) {
  const len = dmxSlice.length % 2 === 0 ? dmxSlice.length : dmxSlice.length + 1;
  const header = Buffer.alloc(18);
  header.write("Art-Net\0", 0, "ascii");
  header.writeUInt16LE(0x5000, 8);
  header.writeUInt16BE(14, 10);
  header.writeUInt8(sequence & 0xff, 12);
  header.writeUInt8(0, 13);
  header.writeUInt16LE(universe & 0xffff, 14);
  header.writeUInt16BE(len, 16);
  const data = Buffer.alloc(len, 0);
  dmxSlice.copy(data);
  return Buffer.concat([header, data]);
}

function sendAllUniverses(count) {
  const socket = dgram.createSocket("udp4");
  let sent = 0;

  function next(uIdx) {
    if (uIdx >= count) {
      socket.close();
      console.log(`✓ ${sent} paquets ArtDMX envoyés → ${TARGET_IP} (univers ${START_UNIVERSE}–${START_UNIVERSE+count-1})`);
      return;
    }
    const universe = START_UNIVERSE + uIdx;
    const ledStart = uIdx * LEDS_PER_UNIV;
    const ledEnd   = Math.min(ledStart + LEDS_PER_UNIV, MATRIX_W * MATRIX_H);
    const slice    = fb.slice(ledStart * CH_PER_LED, ledEnd * CH_PER_LED);
    const packet   = buildArtDmxPacket(universe, slice, uIdx & 0xff);
    socket.send(packet, ARTNET_PORT, TARGET_IP, (err) => {
      if (err) { console.error(`Univers ${universe}: ${err.message}`); }
      sent++;
      setTimeout(() => next(uIdx + 1), 1);
    });
  }
  next(0);
}

function sendSingleUniverseFill(universe, R, G, B) {
  const dmx = Buffer.alloc(512, 0);
  for (let i = 0; i < 512; i += CH_PER_LED) { dmx[i]=R; dmx[i+1]=G; dmx[i+2]=B; }
  const packet = buildArtDmxPacket(universe, dmx, 0);
  const socket = dgram.createSocket("udp4");
  socket.send(packet, ARTNET_PORT, TARGET_IP, (err) => {
    if (err) console.error(err);
    else console.log(`✓ Univers ${universe} rempli → #${R.toString(16).padStart(2,'0')}${G.toString(16).padStart(2,'0')}${B.toString(16).padStart(2,'0')}`);
    socket.close();
  });
}
