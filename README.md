# Grasshopper Live Bridge - Monorepo 🚀

WebSocket-enabled Grasshopper component for live coding and AI-driven design with real-time canvas analysis.

## Components

This monorepo contains four independent components:

| Component | Description | Technology |
|-----------|-------------|------------|
| **[grasshopper-plugin/](./grasshopper-plugin/)** | C# Grasshopper component with WebSocket API | .NET Framework 4.8 |
| **[mcp-server/](./mcp-server/)** | MCP bridge server for Claude Code integration | Node.js |
| **[scripts/](./scripts/)** | Development and testing utilities | Node.js |
| **[tools/](./tools/)** | AI development workflow utilities | Python |

## Quick Start 🚀

### 1. Build & Install Grasshopper Plugin

⚠️ **Important**: Close Rhino completely before building and copying the plugin.

```bash
cd grasshopper-plugin
dotnet build -c Release
Copy-Item "LiveCodingGH\bin\Release\net48\LiveCodingGH.gha" "$env:APPDATA\Grasshopper\Libraries\"
```

**After installation:**
1. **Open Rhino** and **Grasshopper**
2. **Add the component**: Go to **Params → Util** → **"Live Coding Controller (Python)"**
3. **Place it on your canvas** - the WebSocket server starts automatically
4. **Save your .gh file** to ensure the component stays active
5. **Troubleshooting**: If it says "server not running", try disabling and re-enabling the component

### 2. Setup MCP Server for Claude Code

```bash
cd mcp-server
npm install
claude mcp add grasshopper-bridge node "$(pwd)/hybrid-server.js"
```

### 3. Test Connection

```bash
cd scripts
npm install
node test-connection.js ping
node test-connection.js get_canvas_info --timeout=5000
node test-connection.js get_selection
```

## Features ✨

- **🔌 WebSocket API** - Real-time communication with Grasshopper (`ws://localhost:8181/live`)
- **📊 Canvas Analysis** - Get complete JSON representation of your Grasshopper definition  
- **⚡ Live Coding** - Create and update Python components remotely
- **🤖 MCP Integration** - Perfect for AI agents to understand your canvas

## Architecture

```
Claude Code ←→ MCP Server ←→ WebSocket ←→ Grasshopper Component
              (mcp-server/)              (grasshopper-plugin/)
                    ↕️
           Testing Scripts
             (scripts/)
```

## Use Cases 🎯

- **🤖 AI Integration** - MCP servers can analyze canvas structure and provide contextual assistance
- **⚙️ Automation** - Programmatically create and modify Grasshopper definitions
- **⚡ Live Coding** - Real-time Python component development
- **🔗 External Tools** - Connect Grasshopper with other applications and workflows

## Development

Each component has its own README with detailed setup and usage instructions. Start with the grasshopper-plugin to build the core component, then proceed to mcp-server for AI integration.

### Component Dependencies

1. **grasshopper-plugin** - Independent (requires Rhino/Grasshopper)
2. **mcp-server** - Depends on grasshopper-plugin WebSocket server
3. **scripts** - Depends on grasshopper-plugin WebSocket server
4. **tools** - Independent utilities for AI development

---

*Happy coding with AI! 🎉*