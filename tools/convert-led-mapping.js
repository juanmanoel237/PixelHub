#!/usr/bin/env node
/**
 * Convert an Excel-exported CSV mapping into PixelHub EntityMapping CSV.
 *
 * Output format (required by EntityMapping):
 *   entityId,controllerIndex,universe,channel
 *
 * Usage:
 *   node tools/convert-led-mapping.js --in input.csv --out Assets/StreamingAssets/mapping/led_wall_mapping.csv --config Assets/StreamingAssets/config.json
 *
 * Supported input columns (case-insensitive, flexible):
 *   - entityId / id / entity / entity_id
 *   - universe / dmxUniverse / artnetUniverse
 *   - channel / dmxChannel / startChannel
 *   - controllerIp / ip (optional if controllerIndex is provided)
 *   - controllerIndex (optional)
 *
 * Notes:
 *   - If channel appears 1-based (1..512), it's converted to 0-based.
 *   - If controllerIndex is missing, it's resolved from config.json controllers by matching ip.
 */
const fs = require("fs");
const path = require("path");

function arg(name) {
  const i = process.argv.indexOf(name);
  return i >= 0 ? process.argv[i + 1] : undefined;
}

const inPath = arg("--in");
const outPath = arg("--out");
const configPath = arg("--config") || "Assets/StreamingAssets/config.json";

if (!inPath || !outPath) {
  console.error("Missing args. Example:\n  node tools/convert-led-mapping.js --in input.csv --out Assets/StreamingAssets/mapping/led_wall_mapping.csv --config Assets/StreamingAssets/config.json");
  process.exit(1);
}

function parseCsv(text) {
  // Minimal CSV parser: handles commas, quotes, CRLF.
  const rows = [];
  let row = [];
  let cur = "";
  let inQuotes = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    const n = text[i + 1];
    if (inQuotes) {
      if (c === '"' && n === '"') { cur += '"'; i++; continue; }
      if (c === '"') { inQuotes = false; continue; }
      cur += c;
    } else {
      if (c === '"') { inQuotes = true; continue; }
      if (c === ",") { row.push(cur); cur = ""; continue; }
      if (c === "\r" && n === "\n") { row.push(cur); rows.push(row); row = []; cur = ""; i++; continue; }
      if (c === "\n") { row.push(cur); rows.push(row); row = []; cur = ""; continue; }
      cur += c;
    }
  }
  row.push(cur);
  rows.push(row);
  return rows.filter(r => r.length && !(r.length === 1 && r[0].trim() === ""));
}

function normHeader(h) {
  return String(h || "").trim().toLowerCase().replace(/\s+/g, "");
}

function pickIndex(headers, candidates) {
  for (const c of candidates) {
    const idx = headers.indexOf(c);
    if (idx >= 0) return idx;
  }
  return -1;
}

const configJson = JSON.parse(fs.readFileSync(configPath, "utf8"));
const controllers = (configJson.network && configJson.network.controllers) || [];
const ipToIndex = new Map();
controllers.forEach((c, i) => {
  if (c && c.ip) ipToIndex.set(String(c.ip).trim(), i);
});

const inputText = fs.readFileSync(inPath, "utf8");
const rows = parseCsv(inputText);
if (rows.length < 2) {
  console.error("Input CSV seems empty:", inPath);
  process.exit(1);
}

const headers = rows[0].map(normHeader);
const idxEntity = pickIndex(headers, ["entityid", "id", "entity", "entity_id"]);
const idxUniverse = pickIndex(headers, ["universe", "dmxuniverse", "artnetuniverse"]);
const idxChannel = pickIndex(headers, ["channel", "dmxchannel", "startchannel"]);
const idxCtrlIndex = pickIndex(headers, ["controllerindex"]);
const idxIp = pickIndex(headers, ["controllerip", "ip"]);

if (idxEntity < 0 || idxUniverse < 0 || idxChannel < 0) {
  console.error("Missing required columns. Found headers:", headers.join(", "));
  console.error("Need at least: entityId, universe, channel (names can vary).");
  process.exit(1);
}

const outLines = ["entityId,controllerIndex,universe,channel"];

let kept = 0;
let skipped = 0;

for (let r = 1; r < rows.length; r++) {
  const row = rows[r];
  const entityId = parseInt(row[idxEntity], 10);
  const universe = parseInt(row[idxUniverse], 10);
  let channel = parseInt(row[idxChannel], 10);

  if (!Number.isFinite(entityId) || !Number.isFinite(universe) || !Number.isFinite(channel)) { skipped++; continue; }

  // Convert 1-based DMX channel if needed
  if (channel >= 1 && channel <= 512) channel = channel - 1;
  if (channel < 0 || channel > 511) { skipped++; continue; }

  let controllerIndex = -1;
  if (idxCtrlIndex >= 0 && row[idxCtrlIndex] !== undefined && row[idxCtrlIndex] !== "") {
    controllerIndex = parseInt(row[idxCtrlIndex], 10);
  } else if (idxIp >= 0 && row[idxIp]) {
    const ip = String(row[idxIp]).trim();
    controllerIndex = ipToIndex.has(ip) ? ipToIndex.get(ip) : -1;
  }

  if (!Number.isFinite(controllerIndex) || controllerIndex < 0) { skipped++; continue; }

  outLines.push(`${entityId},${controllerIndex},${universe},${channel}`);
  kept++;
}

fs.mkdirSync(path.dirname(outPath), { recursive: true });
fs.writeFileSync(outPath, outLines.join("\n") + "\n", "utf8");

console.log(`Converted mapping: kept=${kept} skipped=${skipped}`);
console.log(`Wrote: ${outPath}`);

