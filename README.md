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
copy "bin\Release\net48\LiveCodingGH.gha" "%APPDATA%\Grasshopper\Libraries\"
```

### 2. Test the API 🧪
```bash
npm install
node scripts/test_connection.js ping
node scripts/test_connection.js get_canvas_info --wait=5000
```

### 3. Use in Grasshopper 🦗
1. Open Grasshopper
2. Add "Live Coding Controller (Python)" component (Params → Util)
3. Your WebSocket server is now running!

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

## Project Overview 🧭

This repository contains multiple components that work together to enable live, AI-assisted design workflows in Grasshopper:

- **Grasshopper Component** (`grasshopper_component/LiveCodingGH`):
  - Builds the `LiveCodingGH.gha` plugin for Grasshopper.
  - Hosts a WebSocket server inside Grasshopper on `ws://localhost:8181/live`.
  - Powers features like canvas introspection and live Python component updates.

- **MCP Server** (`mcp_server/`):
  - Node.js server that exposes your Grasshopper canvas and tools via the Model Context Protocol (MCP) for AI agents.
  - Entry points: `mcp-server.js` (pure MCP), `hybrid-server.js` (MCP + HTTP), `server.js` (HTTP-only playground).
  - Comes with utilities, prompts, and tools that wrap the Grasshopper WebSocket API.

- **Scripts** (`scripts/`):
  - Quick utilities for local testing, e.g. `scripts/test_connection.js` to ping and request canvas info.

- **Docs**: High-level and IDE-specific guides for building and using the bridge.

- **Shared** (`shared/`): Common assets and future shared code.

## MCP Server Docs 📚

If you plan to integrate with an AI agent or run the MCP server, start here:

- **MCP Server README**: [`mcp_server/README.md`](mcp_server/README.md)
- **MCP Start Guide**: [`mcp_server/MCP Start Guide.md`](mcp_server/MCP%20Start%20Guide.md)
- **Development Guides**: [`mcp_server/DEVELOPMENT_GUIDES.md`](mcp_server/DEVELOPMENT_GUIDES.md)

These documents cover running the MCP server, available tools, configuration, and development workflows.

## Use Cases 🎯

- **🤖 AI Integration** - MCP servers can analyze canvas structure and provide contextual assistance
- **⚙️ Automation** - Programmatically create and modify Grasshopper definitions
- **⚡ Live Coding** - Real-time Python component development
- **🔗 External Tools** - Connect Grasshopper with other applications and workflows