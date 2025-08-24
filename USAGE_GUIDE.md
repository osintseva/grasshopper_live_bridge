# Usage Guide: Live Coding Grasshopper Bridge

This guide explains how to use the WebSocket server to interact with your Grasshopper canvas and retrieve canvas information for MCP servers.

## Quick Start

### 1. Install and Setup
1. Follow the [BUILD_INSTRUCTIONS.md](BUILD_INSTRUCTIONS.md) to compile and install the plugin
2. Place the **Live Coding Controller (Python)** component on your Grasshopper canvas
3. The WebSocket server will automatically start on `ws://localhost:8181/live`

### 2. Test Connection

Use the provided test script:
```bash
# Test basic connection
node scripts/test_connection.js ping

# Get canvas information
node scripts/test_connection.js get_canvas_info --wait=5000
```

## WebSocket API

The WebSocket server accepts JSON messages with this format:
```json
{
  "action": "action_name",
  "correlationId": "unique_id",
  "payload": { /* action-specific data */ }
}
```

### Available Actions

#### 1. `ping` - Test Connection
```bash
node scripts/test_connection.js ping
```

**Request:**
```json
{
  "action": "ping",
  "correlationId": "123456",
  "payload": { "t": 1703123456789 }
}
```

**Response:**
```json
{
  "action": "ping_response",
  "correlationId": "123456",
  "status": "queued"
}
```

#### 2. `get_canvas_info` - Retrieve Canvas Definition
This is the main endpoint for MCP servers to understand the Grasshopper canvas.

```bash
node scripts/test_connection.js get_canvas_info --wait=5000
```

**Request:**
```json
{
  "action": "get_canvas_info",
  "correlationId": "123456",
  "payload": {}
}
```

**Response:**
```json
{
  "action": "get_canvas_info_response",
  "correlationId": "abc-def-123",
  "status": "success",
  "data": "{\n  \"Components\": [\n    {\n      \"Id\": 0,\n      \"Name\": \"Number Slider\",\n      \"NickName\": \"N\",\n      \"Description\": \"Numeric slider for single values\",\n      \"IsComponent\": false,\n      \"Inputs\": [],\n      \"Outputs\": [\n        {\n          \"Name\": \"N\",\n          \"TypeName\": \"Number (floating point)\",\n          \"DataPreview\": \"5.00\"\n        }\n      ]\n    }\n  ]\n}"
}
```

The `data` field contains a JSON string representing the entire canvas definition with:
- **Components**: All components and parameters
- **Id**: Unique identifier for each object
- **Name**: Full component name
- **NickName**: Display name
- **Description**: Component description
- **IsComponent**: `true` for components, `false` for parameters
- **Inputs/Outputs**: Connection information and data previews

#### 3. Other Actions
The server also supports live coding actions:

- `create_slider` - Create a new slider
- `create_python_script` - Create Python script component  
- `update_script` - Update existing component code

See the original component for details on these actions.

## Integration with MCP Servers

### Basic MCP Server Integration

Here's how to integrate with an MCP server to provide Grasshopper canvas information:

```javascript
// Example Node.js MCP server integration
const WebSocket = require('ws');

class GrasshopperMCPServer {
  constructor() {
    this.wsUrl = 'ws://localhost:8181/live';
    this.ws = null;
  }

  async getCanvasInfo() {
    return new Promise((resolve, reject) => {
      this.ws = new WebSocket(this.wsUrl);
      
      this.ws.on('open', () => {
        const request = {
          action: 'get_canvas_info',
          correlationId: Date.now().toString(),
          payload: {}
        };
        this.ws.send(JSON.stringify(request));
      });

      this.ws.on('message', (data) => {
        const response = JSON.parse(data.toString());
        if (response.action === 'get_canvas_info_response') {
          const canvasData = JSON.parse(response.data);
          this.ws.close();
          resolve(canvasData);
        }
      });

      this.ws.on('error', reject);
      
      // Timeout after 10 seconds
      setTimeout(() => {
        this.ws.close();
        reject(new Error('Timeout waiting for canvas info'));
      }, 10000);
    });
  }

  async describeCanvas() {
    const canvas = await this.getCanvasInfo();
    
    // Process the canvas data for LLM consumption
    const description = {
      totalComponents: canvas.Components.length,
      components: canvas.Components.map(comp => ({
        name: comp.NickName,
        type: comp.Name,
        isSource: comp.Inputs.length === 0,
        hasOutput: comp.Outputs.length > 0,
        outputData: comp.Outputs.map(out => out.DataPreview).join(', ')
      }))
    };
    
    return description;
  }
}

// Usage
const server = new GrasshopperMCPServer();
server.describeCanvas().then(description => {
  console.log('Canvas Description:', JSON.stringify(description, null, 2));
});
```

### Python Integration Example

```python
import asyncio
import websockets
import json

class GrasshopperBridge:
    def __init__(self, url='ws://localhost:8181/live'):
        self.url = url
    
    async def get_canvas_info(self):
        async with websockets.connect(self.url) as websocket:
            request = {
                'action': 'get_canvas_info',
                'correlationId': str(asyncio.get_event_loop().time()),
                'payload': {}
            }
            
            await websocket.send(json.dumps(request))
            
            async for message in websocket:
                response = json.loads(message)
                if response.get('action') == 'get_canvas_info_response':
                    return json.loads(response['data'])
    
    async def analyze_canvas(self):
        canvas = await self.get_canvas_info()
        
        # Analyze for LLM
        analysis = {
            'summary': f"Canvas contains {len(canvas['Components'])} objects",
            'components': []
        }
        
        for comp in canvas['Components']:
            analysis['components'].append({
                'name': comp['NickName'],
                'type': 'Component' if comp['IsComponent'] else 'Parameter',
                'description': comp['Description'],
                'outputs': [out['DataPreview'] for out in comp['Outputs']]
            })
        
        return analysis

# Usage
bridge = GrasshopperBridge()
analysis = asyncio.run(bridge.analyze_canvas())
print(json.dumps(analysis, indent=2))
```

## Testing

### 1. Basic Functionality Test
```bash
# Test WebSocket connection
node scripts/test_connection.js ping

# Create a test slider
node scripts/test_connection.js create_slider

# Get canvas info (should show the slider)
node scripts/test_connection.js get_canvas_info --wait=5000
```

### 2. Canvas Analysis Test
1. Create a simple Grasshopper definition with:
   - A number slider
   - A point component
   - A line component connecting them
2. Run: `node scripts/test_connection.js get_canvas_info --wait=5000`
3. Verify the JSON output shows all components with correct connections

### 3. Real-time Updates
1. Keep the WebSocket connection open: `--wait=30000`
2. Modify your Grasshopper canvas while connected
3. Request canvas info again to see updates

## Troubleshooting

### WebSocket Connection Issues
- **Port 8181 in use**: Change `WS_PORT` in the component code
- **Firewall blocking**: Allow Rhino through Windows Firewall
- **Component not running**: Verify the Live Coding component is on your canvas

### Canvas Info Issues  
- **Empty response**: Make sure there are components on the canvas
- **Incomplete data**: Some components might not be fully computed - trigger a solve
- **Large data truncated**: Adjust `FULL_DATA_CHAR_LIMIT` in the code if needed

### Performance
- Large canvases (100+ components) may take several seconds to analyze
- Data previews are limited to prevent memory issues
- Consider filtering specific component types if needed