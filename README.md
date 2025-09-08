# Grasshopper Live Bridge 🚀

WebSocket-enabled Grasshopper component for live coding and AI-driven design with real-time canvas analysis.

## Features ✨

- **🔌 WebSocket API** - Real-time communication with Grasshopper (`ws://localhost:8181/live`)
- **📊 Canvas Analysis** - Get complete JSON representation of your Grasshopper definition  
- **⚡ Live Coding** - Create and update Python components remotely
- **🤖 MCP Integration** - Perfect for AI agents to understand your canvas

## Quick Start 🚀

### 1. Build & Install 🔨
```bash
cd grasshopper_component/LiveCodingGH
dotnet build -c Release
Copy-Item "bin\Release\net48\LiveCodingGH.gha" "$env:APPDATA\Grasshopper\Libraries\"
```

### 2. Use in Grasshopper 🦗
1. Open Grasshopper
2. Add "Live Coding Controller (Python)" component (Params → Util)
3. Your WebSocket server is now running!

### 3. Test the API 🧪
```bash
npm install
node scripts/test_connection.js ping
node scripts/test_connection.js get_canvas_info --wait=5000
```


## WebSocket API 🔌

**Endpoint:** `ws://localhost:8181/live`

**Get Canvas Info:**
```json
{
  "action": "get_canvas_info",
  "correlationId": "unique_id",
  "payload": {}
}
```

**Response:** Complete canvas definition with all components, connections, and data previews.

## Documentation 📖

- **[VSCODE_BUILD_GUIDE.md](VSCODE_BUILD_GUIDE.md)** - Detailed build instructions and development workflow
- **[USAGE_GUIDE.md](USAGE_GUIDE.md)** - Complete API documentation and integration examples

## Use Cases 🎯

- **🤖 AI Integration** - MCP servers can analyze canvas structure and provide contextual assistance
- **⚙️ Automation** - Programmatically create and modify Grasshopper definitions
- **⚡ Live Coding** - Real-time Python component development
- **🔗 External Tools** - Connect Grasshopper with other applications and workflows