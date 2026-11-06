// Captures the SubWake window's page over the Chrome DevTools Protocol, for the README screenshots.
// Start the app with WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222 first (see Docs/WORKFLOW.md).
//   node Tools/Screenshot.mjs <out.png> [--full] [--eval "<js run first>"] [--port 9222]
import { writeFileSync } from 'node:fs';

const args = process.argv.slice(2);
const out = args[0];
const full = args.includes('--full');
const evalAt = args.indexOf('--eval');
const script = evalAt >= 0 ? args[evalAt + 1] : null;
const portAt = args.indexOf('--port');
const port = portAt >= 0 ? args[portAt + 1] : '9222';

const pages = await (await fetch(`http://127.0.0.1:${port}/json`)).json();
const page = pages.find(p => p.type === 'page' && p.url.includes('subwake.local'));
if (!page) throw new Error('SubWake window not found on the debugging port.');

const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = reject; });
let id = 0;
const pending = new Map();
ws.onmessage = e => {
  const m = JSON.parse(e.data);
  if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
};
const call = (method, params = {}) => new Promise(resolve => { const n = ++id; pending.set(n, resolve); ws.send(JSON.stringify({ id: n, method, params })); });
const wait = ms => new Promise(r => setTimeout(r, ms));

if (script) { await call('Runtime.evaluate', { expression: script, awaitPromise: true }); await wait(800); }
let params = { format: 'png' };
if (full) {
  const metrics = (await call('Page.getLayoutMetrics')).result;
  const size = metrics.cssContentSize ?? metrics.contentSize;
  const view = metrics.cssLayoutViewport ?? metrics.layoutViewport;
  params = { ...params, captureBeyondViewport: true, clip: { x: 0, y: 0, width: view.clientWidth, height: size.height, scale: 1 } };
}
const shot = await call('Page.captureScreenshot', params);
if (!shot.result) throw new Error(JSON.stringify(shot.error));
writeFileSync(out, Buffer.from(shot.result.data, 'base64'));
console.log(`Saved ${out}`);
ws.close();
