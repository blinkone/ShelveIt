// Checks the share code format against a code produced by the C# side (Logic/ShareCode.cs).
// Format: "SHELVE1:" + base64url without padding of gzip(UTF-8 compact JSON of the payload).
// usage: node preview/tools/sharecode-check.js [code-file-to-write]
// With an argument it also writes a Node-made code (wrapped over several lines) that the C#
// decoder must accept.
const zlib = require("zlib");
const fs = require("fs");

const PREFIX = "SHELVE1:";

// Made by ShareCode.Encode for the sample payload below.
const CSHARP_CODE = "SHELVE1:H4sIAAAAAAAEAH2PPU7DQBCFr7Ka2opsoEBbBokmRZBwaFAUjWcHexJ7be0OSRH5LnSIc_hirHFq2qfv_V0hUsMdgi0y8NgxWHgS1enLnJmoETS7DWTgOFKQQaX3iSg5ahIV6wj2HaogKrGBfQZd7944xAXLV8UqTxzGyDqTVzglef0prRNfvwT-wMruNoeSQ0DiQ14kegD7WNzdP4zZgr8qqtC2OjLpzbJmT42ZfhY6H1PxgHTCmpcWcbeMtFG0nU9tW2fK_uLNM07fmO7AnyvwWfgyn_h31X78BY5TKkwpAQAA";

const SAMPLE = {
  schema: 1,
  name: "Città vecchia UK",
  description: "Test",
  tags: ["british"],
  modVersion: "0.1.0",
  assets: [
    { k: "BuildingPrefab:UK_Terrace_01", p: 81234 },
    { k: "StaticObjectPrefab:Bench é", p: 0 },
  ],
  packages: [{ id: 81234, title: "Old Town Façades" }],
  preview: ["BuildingPrefab:UK_Terrace_01"],
};

function encode(payload) {
  const gz = zlib.gzipSync(Buffer.from(JSON.stringify(payload), "utf8"));
  return PREFIX + gz.toString("base64").replace(/=+$/, "").replace(/\+/g, "-").replace(/\//g, "_");
}

function decode(code) {
  const text = code.replace(/\s+/g, "");
  if (text.slice(0, PREFIX.length).toUpperCase() !== PREFIX) throw new Error("missing prefix");
  const b64 = text.slice(PREFIX.length).replace(/-/g, "+").replace(/_/g, "/");
  const payload = JSON.parse(zlib.gunzipSync(Buffer.from(b64, "base64")).toString("utf8"));
  if (payload.schema !== 1) throw new Error("unknown schema " + payload.schema);
  return payload;
}

const fromCsharp = decode(CSHARP_CODE);
if (JSON.stringify(fromCsharp) !== JSON.stringify(SAMPLE)) {
  console.error("C# code does not decode to the sample:", JSON.stringify(fromCsharp));
  process.exit(1);
}

// The JSON text is the same, so only gzip header bytes may differ between the two encoders.
const fromNode = encode(SAMPLE);
if (JSON.stringify(decode(fromNode)) !== JSON.stringify(SAMPLE)) process.exit(1);

const wrapped = fromNode.match(/.{1,40}/g).join("\r\n  ");
if (JSON.stringify(decode(wrapped)) !== JSON.stringify(SAMPLE)) process.exit(1);

if (process.argv[2]) fs.writeFileSync(process.argv[2], wrapped);
console.log("share code format ok (C# decode, Node round trip, line breaks tolerated)");
