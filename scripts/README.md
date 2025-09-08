# Scripts - Development & Testing Utilities

Node.js scripts for testing WebSocket connections and validating the Grasshopper plugin functionality.

## Prerequisites

- **Node.js v18+** installed
- **Grasshopper plugin** running with WebSocket server on port 8181
- See `../grasshopper-plugin/README.md` for plugin setup

## Quick Setup

All commands should be run from this directory (`scripts/`):

```bash
npm install
```

## Available Scripts

### Connection Testing

**Test basic connectivity:**
```bash
node test-connection.js ping
```

**Get canvas information:**
```bash
node test-connection.js get_canvas_info
```

**Get canvas info with timeout:**
```bash
node test-connection.js get_canvas_info --wait=5000
```

### Script Options

- `--wait=<ms>` - Wait time in milliseconds before sending requests (default: 1000)
- `--timeout=<ms>` - Request timeout in milliseconds (default: 10000)

## Test Connection Script

The main testing utility (`test-connection.js`) supports these actions:

| Action | Description | Example |
|--------|-------------|---------|
| `ping` | Test basic WebSocket connection | `node test-connection.js ping` |
| `get_canvas_info` | Retrieve complete canvas state | `node test-connection.js get_canvas_info` |
| `get_selection` | Get currently selected components | `node test-connection.js get_selection` |

### Example Outputs

**Successful ping:**
```json
{
  "status": "success",
  "message": "Connected to Grasshopper WebSocket",
  "timestamp": "2024-01-15T10:30:00.000Z"
}
```

**Canvas info response:**
```json
{
  "status": "success",
  "data": {
    "Components": [...],
    "Connections": [...],
    "Groups": [...],
    "Statistics": {
      "ComponentCount": 15,
      "ConnectionCount": 8
    }
  }
}
```

## Workflow Examples

### 1. Basic Connection Test
```bash
# Test if Grasshopper plugin is responding
node test-connection.js ping
```

### 2. Canvas Analysis
```bash
# Get full canvas state for analysis
node test-connection.js get_canvas_info --wait=2000
```

### 3. Development Workflow
```bash
# Install dependencies
npm install

# Quick connection test
npm run test:connection

# Test canvas retrieval
npm run test:canvas
```

## Troubleshooting

### Connection Refused
```
Error: connect ECONNREFUSED 127.0.0.1:8181
```

**Solutions:**
1. **Check Grasshopper**: Ensure Rhino and Grasshopper are running
2. **Check plugin**: Verify the Live Coding Controller component is on the canvas
3. **Check port**: Ensure nothing else is using port 8181

### Timeout Errors
```
Error: WebSocket connection timeout
```

**Solutions:**
1. **Increase timeout**: Use `--timeout=15000` for slower systems
2. **Add wait time**: Use `--wait=3000` to let Grasshopper initialize
3. **Check canvas**: Ensure canvas has components to analyze

### WebSocket Protocol Errors
```
Error: Invalid WebSocket frame
```

**Solutions:**
1. **Restart Grasshopper**: Close and reopen Grasshopper
2. **Reinstall plugin**: See `../grasshopper-plugin/README.md`
3. **Check plugin version**: Ensure you have the latest build

## Package.json Scripts

The following npm scripts are available:

```bash
# Test basic connection
npm run test:connection

# Test canvas retrieval with delay
npm run test:canvas

# Run all tests
npm test
```

## Adding New Tests

To add new test scripts:

1. **Create script file** in the `scripts/` directory
2. **Add to package.json** scripts section
3. **Follow the pattern** used in `test-connection.js`
4. **Update this README** with usage examples

### Example Test Script Structure

```javascript
#!/usr/bin/env node
const WebSocket = require('ws');

async function testNewFeature() {
  const ws = new WebSocket('ws://localhost:8181/live');
  
  ws.on('open', () => {
    ws.send(JSON.stringify({
      action: 'your_action',
      correlationId: Date.now().toString(),
      payload: {}
    }));
  });
  
  ws.on('message', (data) => {
    const response = JSON.parse(data);
    console.log('Response:', response);
    ws.close();
  });
}

testNewFeature().catch(console.error);
```

## Integration with Other Components

### With MCP Server
```bash
# Test Grasshopper connection before setting up MCP server
cd ../scripts && node test-connection.js ping
cd ../mcp-server && npm install
```

### With Development Tools
```bash
# Use with AI development workflow
cd ../scripts && node test-connection.js get_canvas_info > ../tools/canvas-dump.json
cd ../tools && python analyze-canvas.py
```

## Project Structure

```
scripts/
├── package.json              # Dependencies and npm scripts
├── test-connection.js         # Main testing utility
├── testing/                   # Additional test files
└── README.md                 # This file
```

## Next Steps

After verifying the scripts work:

1. **Setup MCP Server**: Follow `../mcp-server/README.md` 
2. **Use AI Tools**: Try utilities in `../tools/README.md`
3. **Develop Further**: Add custom test scripts for your use cases