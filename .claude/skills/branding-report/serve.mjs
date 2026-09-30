// branding-report / serve.mjs
// Tiny static server for the interactive viewer. Serves the viewer HTML at "/" and
// the survey output (manifest.json + PNGs) from the given output directory.
//
// Usage: node serve.mjs <outDir> [port]
import fs from "node:fs";
import http from "node:http";
import path from "node:path";

const outDir = path.resolve(process.argv[2] || "branding-report-out");
const port = Number(process.argv[3]) || 8799;
const viewerHtml = path.join(import.meta.dirname, "viewer", "index.html");

const TYPES = {
    ".html": "text/html; charset=utf-8",
    ".json": "application/json; charset=utf-8",
    ".png": "image/png",
    ".svg": "image/svg+xml",
    ".css": "text/css",
    ".js": "text/javascript",
};

http.createServer((req, res) => {
    let urlPath = decodeURIComponent(req.url.split("?")[0]);
    let filePath;
    if (urlPath === "/" || urlPath === "/index.html") {
        filePath = viewerHtml;
    } else {
        // prevent path traversal; serve from outDir
        const rel = path.normalize(urlPath).replace(/^(\.\.[/\\])+/, "");
        filePath = path.join(outDir, rel);
    }
    fs.readFile(filePath, (err, buf) => {
        if (err) {
            res.writeHead(404);
            res.end("Not found: " + urlPath);
            return;
        }
        res.writeHead(200, {
            "Content-Type":
                TYPES[path.extname(filePath)] || "application/octet-stream",
        });
        res.end(buf);
    });
}).listen(port, () => {
    console.log(
        `branding-report viewer: http://localhost:${port}/  (serving ${outDir})`,
    );
});
