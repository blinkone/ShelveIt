// Reads the class name maps of the game's style modules out of the installed UI bundle, so the
// preview mocks can use the real class names with the real stylesheet.
// usage: node extract-classes.js "<game UI folder>" <out.json>
const fs = require("fs");
const path = require("path");
const js = fs.readFileSync(path.join(process.argv[2], "index.js"), "utf8");
const out = {};
const re = /Q\.add\("(game-ui\/[^"]+\.module\.scss)",\{get classes\(\)\{return ([\w$]+)\}/g;
let m;
while ((m = re.exec(js))) {
  const decl = new RegExp("(?:let|const|var|,) *" + m[2].split("$").join("[$]") + "=([{][^{}]*[}])", "g");
  let d, last = null;
  const head = js.slice(Math.max(0, m.index - 6000), m.index);
  while ((d = decl.exec(head))) last = d[1];
  if (!last) continue;
  try {
    out[m[1]] = Function("return " + last)();
  } catch (e) {}
}
fs.writeFileSync(process.argv[3], JSON.stringify(out));
console.log(Object.keys(out).length + " style modules");
