// Local-only test host. No dependencies. Serves only the requested content root.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const args = process.argv.slice(2);
const arg = (name, fallback) => { const i = args.indexOf(name); return i < 0 ? fallback : args[i + 1]; };
const root = path.resolve(arg('--root', 'NowFrameWork/Build/PCRegression/ServerData'));
const port = Number(arg('--port', '18182'));
const logPath = arg('--log', '');
let delayMs = Number(arg('--delay-ms', '0'));
const shutdownToken = arg('--shutdown-token', '');
const log = record => {
  const line = JSON.stringify({ utc: new Date().toISOString(), ...record });
  if (logPath) fs.appendFileSync(logPath, line + '\n');
  process.stdout.write(line + '\n');
};
const server = http.createServer((req, res) => {
  const started = Date.now();
  const url = new URL(req.url, 'http://127.0.0.1');
  if (url.pathname === '/__shutdown') {
    if (req.method !== 'POST' || !shutdownToken || req.headers['x-pc-probe-token'] !== shutdownToken) {
      res.writeHead(403); res.end(); return;
    }
    res.end('closing');
    server.close();
    server.closeIdleConnections?.();
    return;
  }
  if (url.pathname === '/__status' || url.pathname === '/__control') {
    if (url.pathname === '/__control') delayMs = Math.max(0, Math.min(15000, Number(url.searchParams.get('delayMs')) || 0));
    res.setHeader('Content-Type', 'application/json');
    res.end(JSON.stringify({ root, delayMs }));
    return;
  }
  let file;
  try { file = path.resolve(root, '.' + decodeURIComponent(url.pathname)); }
  catch { res.writeHead(400); res.end(); return; }
  if (!file.startsWith(root + path.sep)) { res.writeHead(403); res.end(); return; }
  const respond = () => {
    let stat;
    try { stat = fs.statSync(file); } catch { res.writeHead(404); res.end(); log({ path: url.pathname, status: 404 }); return; }
    if (!stat.isFile()) { res.writeHead(404); res.end(); return; }
    res.setHeader('Content-Length', stat.size);
    res.setHeader('Cache-Control', 'no-cache');
    res.setHeader('Content-Type', file.endsWith('.json') ? 'application/json' : 'application/octet-stream');
    res.on('finish', () => log({ path: url.pathname, status: 200, bytes: stat.size, milliseconds: Date.now() - started }));
    const stream = fs.createReadStream(file);
    stream.on('error', () => res.destroy());
    stream.pipe(res);
  };
  // Catalog/hash remain fast so cancellation tests target bundle operations.
  if (file.endsWith('.bundle') && delayMs > 0) setTimeout(respond, delayMs);
  else respond();
}).listen(port, '127.0.0.1', () => log({ event: 'listening', root, port, delayMs }));
