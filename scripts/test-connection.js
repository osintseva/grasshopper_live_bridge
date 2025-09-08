// scripts/test_connection.js
// Usage:
//   node scripts/test_connection.js                       # ping (default)
//   node scripts/test_connection.js create_slider         # create a slider
//   node scripts/test_connection.js get_canvas_info       # get canvas info
//   node scripts/test_connection.js update_script <GUID> <path-to-py>
// Options:
//   --wait=10000   keep the socket open N ms (default 800ms)
// Env:
//   GH_WS_URL=ws://localhost:8181/live   (override server URL)

const fs = require('fs');
const path = require('path');
const WebSocket = require('ws');

const URL = process.env.GH_WS_URL || 'ws://localhost:8181/live';

// ---- parse CLI ----
const args = process.argv.slice(2);
let cmd = 'ping';
let waitMs = 800; // short default so we auto-exit quickly
let guid = null;
let filePath = null;

for (const a of args) {
  if (a.startsWith('--wait=')) {
    const v = parseInt(a.split('=')[1] || '0', 10);
    if (!Number.isNaN(v) && v >= 0) waitMs = v;
  } else if (['ping', 'create_slider', 'create_python_script', 'create_csharp_python', 'update_script', 'get_canvas_info'].includes(a)) {
    cmd = a;
  } else if ((cmd === 'update_script' || cmd === 'create_python_script') && !guid && cmd === 'update_script') {
    guid = a;
  } else if ((cmd === 'update_script' && guid && !filePath) || (cmd === 'create_python_script' && !filePath) || (cmd === 'create_csharp_python' && !filePath)) {
    filePath = a;
  }
}

function nowId() { return Date.now().toString(); }
function send(ws, obj) {
  const json = JSON.stringify(obj);
  console.log('>>', json);
  ws.send(json);
}

function run() {
  console.log('[i] Connecting to', URL);
  const ws = new WebSocket(URL);

  ws.on('open', () => {
    console.log('[i] OPEN');

    if (cmd === 'ping') {
      send(ws, { action: 'ping', correlationId: nowId(), payload: { t: Date.now() } });
    } else if (cmd === 'create_slider') {
      send(ws, {
        action: 'create_slider',
        correlationId: nowId(),
        payload: { x: Math.random() * 400 + 100, y: Math.random() * 300 + 100, nickname: 'From test-connection.js' }
      });
    } else if (cmd === 'create_python_script') {
      const code = filePath ? fs.readFileSync(path.resolve(filePath), 'utf8') : "print('i finally managed')";
      send(ws, {
        action: 'create_python_script',
        correlationId: nowId(),
        payload: { 
          x: Math.random() * 400 + 100, 
          y: Math.random() * 300 + 100, 
          code: code 
        }
      });
    } else if (cmd === 'create_csharp_python') {
      const code = filePath ? fs.readFileSync(path.resolve(filePath), 'utf8') : "print('i finally managed')";
      send(ws, {
        action: 'create_csharp_python',
        correlationId: nowId(),
        payload: { 
          x: Math.random() * 400 + 100, 
          y: Math.random() * 300 + 100, 
          code: code 
        }
      });
    } else if (cmd === 'update_script') {
      if (!guid || !filePath) {
        console.error('Usage: node scripts/test_connection.js update_script <GUID> <path-to-py> [--wait=ms]');
        process.exit(1);
      }
      const code = fs.readFileSync(path.resolve(filePath), 'utf8');
      send(ws, {
        action: 'update_script',
        correlationId: nowId(),
        payload: { componentId: guid, code }
      });
    } else if (cmd === 'get_canvas_info') {
      send(ws, {
        action: 'get_canvas_info',
        correlationId: nowId(),
        payload: {}
      });
    } else {
      console.error('Unknown command:', cmd);
      ws.close();
      return;
    }

    // auto-close after waitMs so the terminal is released
    if (waitMs > 0) {
      setTimeout(() => {
        console.log('[i] Auto-closing after', waitMs, 'ms');
        try { ws.close(); } catch {}
      }, waitMs);
    }
  });

  ws.on('message', (data) => {
    try {
      const msg = JSON.parse(data.toString());
      if (msg.action === 'get_canvas_info_response' && msg.data) {
        console.log('<<', JSON.stringify(msg, null, 2));
      } else {
        console.log('<<', msg);
      }
    } catch {
      console.log('<<', data.toString());
    }
  });

  ws.on('error', (err) => console.error('[!] ERROR', err.message));
  ws.on('close', () => console.log('[i] CLOSE'));
}

run();
