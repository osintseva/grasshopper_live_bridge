# Grasshopper Live Bridge 🚀

A three-tier system for live coding and AI-driven design in Grasshopper with WebSocket API and canvas analysis for MCP servers.

## Features ✨

- **WebSocket Server** - Real-time communication with Grasshopper
- **Canvas Analysis** - Get detailed JSON of your entire Grasshopper definition  
- **Live Coding** - Create and update Python components remotely
- **MCP Integration** - Perfect for AI agents to understand your canvas

## Components

1. **LiveCodingGH Plugin** - Grasshopper component with WebSocket server
2. **Node.js Test Scripts** - WebSocket API testing and examples
3. **MCP Server Support** - Canvas info endpoint for AI integration

## Quick Start 🚀

### 1. Build & Install Plugin

**Using VS Code (Recommended):**
```bash
cd grasshopper_component/LiveCodingGH
dotnet build -c Release
copy "bin\Release\net48\LiveCodingGH.gha" "%APPDATA%\Grasshopper\Libraries\"
```

**📖 Detailed Guide:** [VSCODE_BUILD_GUIDE.md](VSCODE_BUILD_GUIDE.md)

### 2. Setup Test Environment
```bash
npm install
```

### 3. Test WebSocket API
```bash
# Test connection
node scripts/test_connection.js ping

# Get canvas information (main feature for MCP)
node scripts/test_connection.js get_canvas_info --wait=5000

# Create components remotely
node scripts/test_connection.js create_slider
```

## WebSocket API 🔌

**Endpoint:** `ws://localhost:8181/live`

**Get Canvas Info (for MCP servers):**
```javascript
{
  "action": "get_canvas_info",
  "correlationId": "unique_id", 
  "payload": {}
}
```

**Response:**
```javascript
{
  "action": "get_canvas_info_response",
  "status": "success",
  "data": "{\"Components\":[...]}" // Full canvas JSON
}
```

## Documentation 📖

- **[VSCODE_BUILD_GUIDE.md](VSCODE_BUILD_GUIDE.md)** - Modern VS Code development workflow
- **[BUILD_INSTRUCTIONS.md](BUILD_INSTRUCTIONS.md)** - Visual Studio build instructions  
- **[USAGE_GUIDE.md](USAGE_GUIDE.md)** - Complete API documentation and MCP integration examples

## Use Cases

- **MCP Servers** - AI agents can query canvas structure and provide contextual help
- **Live Coding** - Real-time Python component creation and updates
- **Automation** - Programmatic Grasshopper definition creation
- **Integration** - Connect Grasshopper with external tools and workflows