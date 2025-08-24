// scripts/create_python_with_io.js
// Full file — run with: node scripts/create_python_with_io.js path/to/test.py
const WebSocket = require('ws');
const fs = require('fs');
const path = require('path');

const filePath = process.argv[2];
if (!filePath) {
  console.error('Usage: node scripts/create_python_with_io.js <path-to-py>');
  process.exit(1);
}

const abs = path.resolve(filePath);
const code = fs.readFileSync(abs, 'utf8');

const ws = new WebSocket('ws://localhost:8181/live');

ws.on('open', () => {
  console.log('Connected to Grasshopper Live Coding server.');
  const payload = {
    action: 'create_python_with_io',
    correlationId: `cid-${Date.now()}`,
    payload: { x: 320, y: 260, code },
    param_definitions: [
      { type: 'input', name: 'N', access: 'item' },
      // IMPORTANT: do NOT include "out". It's reserved by GhPython.
      // Ask for a typed Curve output for nicer sockets in GH:
      { type: 'output', name: 'circles', typehint: 'curve', access: 'tree' },
      { type: 'output', name: 'a',       typehint: 'generic', access: 'item' }, // status
    ],
  };
  ws.send(JSON.stringify(payload));
});

ws.on('message', (data) => {
  console.log('Received:', data.toString());
  ws.close();
});

ws.on('error', (err) => console.error('WebSocket error:', err.message));
ws.on('close', () => console.log('Disconnected.'));
