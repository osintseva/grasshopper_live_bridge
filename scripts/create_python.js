// scripts/create_python.js
// Simple script to create a self-managing Python component
// Usage: node scripts/create_python.js path/to/script.py [x] [y]

const WebSocket = require('ws');
const fs = require('fs');
const path = require('path');

// Parse arguments
const filePath = process.argv[2];
if (!filePath) {
  console.error('Usage: node scripts/create_python.js <path-to-py> [x] [y]');
  console.error('Example: node scripts/create_python.js scripts/test.py 320 260');
  process.exit(1);
}

// Optional position arguments
const x = parseFloat(process.argv[3]) || 320;
const y = parseFloat(process.argv[4]) || 260;

// Read the Python script
const abs = path.resolve(filePath);
if (!fs.existsSync(abs)) {
  console.error(`File not found: ${abs}`);
  process.exit(1);
}

const code = fs.readFileSync(abs, 'utf8');

// Connect to Grasshopper
const ws = new WebSocket('ws://localhost:8181/live');

ws.on('open', () => {
  console.log('Connected to Grasshopper Live Coding server.');
  console.log(`Creating Python component at (${x}, ${y})`);
  
  // Extract script name from file path
  const scriptName = path.basename(filePath, '.py');
  
  const payload = {
    action: 'create_python',
    correlationId: `py-${Date.now()}`,
    payload: { 
      x: x, 
      y: y, 
      code: code,
      script_name: scriptName 
    }
  };
  
  ws.send(JSON.stringify(payload));
});

ws.on('message', (data) => {
  const response = JSON.parse(data.toString());
  console.log('Response:', response);
  
  if (response.status === 'queued') {
    console.log('✓ Python component created successfully');
  } else if (response.action === 'error') {
    console.error('✗ Error:', response.message);
  }
  
  ws.close();
});

ws.on('error', (err) => {
  console.error('WebSocket error:', err.message);
  process.exit(1);
});

ws.on('close', () => {
  console.log('Disconnected.');
});