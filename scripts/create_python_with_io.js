const WebSocket = require('ws');
const fs = require('fs');

// Get the python script file path from command line arguments
const filePath = process.argv[2];
if (!filePath) {
  console.error('Error: Please provide a path to a Python script file.');
  process.exit(1);
}

const code = fs.readFileSync(filePath, 'utf8');
const ws = new WebSocket('ws://localhost:8181/live');

ws.on('open', function open() {
  console.log('Connected to Grasshopper Live Coding server.');

  const payload = {
    action: 'create_python_with_io',
    correlationId: `cid-${Date.now()}`,
    payload: {
      x: 300,
      y: 300,
      code: code,
    },
    param_definitions: [
      {
        type: 'input',
        name: 'N',
        access: 'item', // 'item', 'list', or 'tree'
      },
      {
        type: 'output',
        name: 'circles',
      },
      // The default 'a' or 'out' output will still exist,
      // but this adds a new one named 'circles'.
    ],
  };

  const message = JSON.stringify(payload, null, 2);
  console.log('Sending message:\n', message);
  ws.send(message);
});

ws.on('message', function incoming(data) {
  console.log('Received from server:', data.toString());
  ws.close(); // Close connection after getting a response
});

ws.on('error', function error(err) {
  console.error('WebSocket error:', err.message);
});

ws.on('close', function close() {
  console.log('Disconnected from server.');
});