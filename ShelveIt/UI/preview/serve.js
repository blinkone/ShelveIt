// Static server for the preview. Routes:
//   /         preview/index.html
//   /dist/    the preview bundle
//   /data/    fixtures
//   /game/    the installed game's UI folder (stylesheet, fonts, Media icons)
//   /uil/     the Unified Icon Library mod's icons, when it is installed
// usage: node serve.js [port]   (CS2_INSTALL, GAME_UI and UIL_ICONS override the default folders)
const http = require("http");
const fs = require("fs");
const path = require("path");

const port = Number(process.argv[2] || process.env.PORT || 5517);

// CS2_INSTALL is the game folder (default: the standard Steam path); GAME_UI overrides the UI folder.
const gameUi =
  process.env.GAME_UI ||
  path.join(
    process.env.CS2_INSTALL || "C:/Program Files (x86)/Steam/steamapps/common/Cities Skylines II",
    "Cities2_Data",
    "Content",
    "Game",
    "UI"
  );

function findUil() {
  if (process.env.UIL_ICONS) {
    return process.env.UIL_ICONS;
  }

  const mods = path.join(
    process.env.USERPROFILE || "",
    "AppData",
    "LocalLow",
    "Colossal Order",
    "Cities Skylines II",
    ".cache",
    "Mods",
    "mods_subscribed"
  );

  try {
    const dir = fs.readdirSync(mods).find((name) => name.startsWith("74417_"));

    return dir ? path.join(mods, dir, "Icons") : "";
  } catch {
    return "";
  }
}

const roots = {
  dist: path.join(__dirname, "dist"),
  data: path.join(__dirname, "data"),
  game: path.normalize(gameUi),
  uil: findUil(),
};

const types = {
  ".html": "text/html",
  ".js": "text/javascript",
  ".css": "text/css",
  ".json": "application/json",
  ".svg": "image/svg+xml",
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".ttf": "font/ttf",
  ".otf": "font/otf",
  ".woff": "font/woff",
  ".woff2": "font/woff2",
};

http
  .createServer((req, res) => {
    const url = decodeURIComponent((req.url || "/").split("?")[0]);
    let file;

    if (url === "/" || url === "/index.html") {
      file = path.join(__dirname, "index.html");
    } else {
      const [, root, ...rest] = url.split("/");
      const base = roots[root];

      if (base) {
        file = path.normalize(path.join(base, ...rest));

        if (!file.startsWith(base)) {
          file = undefined;
        }
      }
    }

    if (!file || !fs.existsSync(file) || !fs.statSync(file).isFile()) {
      res.writeHead(404);
      res.end("not found");
      return;
    }

    res.writeHead(200, {
      "Content-Type": types[path.extname(file).toLowerCase()] || "application/octet-stream",
      "Cache-Control": "no-store",
    });
    fs.createReadStream(file).pipe(res);
  })
  .listen(port, () => console.log("Shelve It preview on http://localhost:" + port + "/"));
