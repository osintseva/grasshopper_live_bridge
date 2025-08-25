# Usage Guide 📚

Complete API documentation and integration examples for the Grasshopper Live Bridge.

## Quick Start 🚀

1. Install the plugin: [VSCODE_BUILD_GUIDE.md](VSCODE_BUILD_GUIDE.md)
2. Add "Live Coding Controller (Python)" component to your Grasshopper canvas
3. WebSocket server starts automatically on `ws://localhost:8181/live`

## WebSocket API 🔌

### Message Format
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

#### 2. `get_canvas_info` - Get Canvas Definition  
**The main feature for MCP servers and AI integration.**

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
  "correlationId": "123456",
  "status": "success",
  "data": "{\"Components\":[{\"Id\":0,\"Name\":\"Number Slider\",\"NickName\":\"N\",\"Description\":\"Numeric slider for single values\",\"IsComponent\":false,\"Inputs\":[],\"Outputs\":[{\"Name\":\"N\",\"TypeName\":\"Number (floating point)\",\"DataPreview\":\"5.00\"}]}]}"
}
```

The `data` field contains a JSON string with:
- **Components**: All components and parameters
- **Id**: Unique identifier for each object
- **Name/NickName**: Component names
- **IsComponent**: `true` for components, `false` for parameters  
- **Inputs/Outputs**: Connection information and data previews

#### 3. Live Coding Actions
- `create_slider` - Create number slider
- `create_python_script` - Create Python component  
- `update_script` - Update component code

## Integration Examples 🔗

### MCP Server Integration 🤖
```javascript
const WebSocket = require('ws');

class GrasshopperMCPServer {
  async getCanvasInfo() {
    return new Promise((resolve, reject) => {
      const ws = new WebSocket('ws://localhost:8181/live');
      
      ws.on('open', () => {
        ws.send(JSON.stringify({
          action: 'get_canvas_info',
          correlationId: Date.now().toString(),
          payload: {}
        }));
      });

      ws.on('message', (data) => {
        const response = JSON.parse(data);
        if (response.action === 'get_canvas_info_response') {
          const canvas = JSON.parse(response.data);
          ws.close();
          resolve(canvas);
        }
      });

      ws.on('error', reject);
      setTimeout(() => reject(new Error('Timeout')), 10000);
    });
  }

  async analyzeCanvas() {
    const canvas = await this.getCanvasInfo();
    
    return {
      totalComponents: canvas.Components.length,
      components: canvas.Components.map(comp => ({
        name: comp.NickName,
        type: comp.Name,
        outputs: comp.Outputs.map(out => out.DataPreview)
      }))
    };
  }
}

// Usage
const server = new GrasshopperMCPServer();
const analysis = await server.analyzeCanvas();
console.log(analysis);
```

### Python Integration 🐍
```python
import asyncio
import websockets
import json

async def get_grasshopper_canvas():
    uri = 'ws://localhost:8181/live'
    
    async with websockets.connect(uri) as websocket:
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

# Usage
canvas = asyncio.run(get_grasshopper_canvas())
print(f"Canvas has {len(canvas['Components'])} components")
```

## Testing 🧪

### Basic Test
```bash
# Test connection
node scripts/test_connection.js ping

# Create test components  
node scripts/test_connection.js create_slider

# Get canvas data
node scripts/test_connection.js get_canvas_info --wait=5000
```

### Canvas Analysis Test
1. Create a simple Grasshopper definition with sliders, components, connections
2. Run `get_canvas_info` 
3. Verify JSON output shows all components with correct data

## Troubleshooting 🔧

### WebSocket Issues
- **Port 8181 in use**: Change `WS_PORT` in component code
- **Connection fails**: Check Windows Firewall, ensure component is on canvas
- **No response**: Verify Grasshopper document is open with components

### Canvas Info Issues  
- **Empty response**: Add components to canvas, trigger solve (F5)
- **Large data**: May take several seconds for complex definitions
- **Missing connections**: Ensure all wires are properly connected

## Performance Notes ⚡

- Large canvases (100+ components) may take 5-10 seconds to analyze
- Data previews are automatically truncated to prevent memory issues  
- WebSocket server runs continuously while component is active