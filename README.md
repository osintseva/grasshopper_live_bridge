# Grasshopper Live Bridge - Monorepo 🚀

WebSocket-enabled Grasshopper component for live coding and AI-driven design with real-time canvas analysis.

## Components

This monorepo contains four independent components:

| Component                                        | Description                                 | Technology         |
| ------------------------------------------------ | ------------------------------------------- | ------------------ |
| **[grasshopper-plugin/](./grasshopper-plugin/)** | C# Grasshopper component with WebSocket API | .NET Framework 4.8 |
| **[mcp-server/](./mcp-server/)**                 | 🤖 AI Bridge for Claude Code integration     | Node.js            |
| **[scripts/](./scripts/)**                       | Development and testing utilities           | Node.js            |


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

### 2. Setup AI Bridge for Claude Code

```bash
cd mcp-server
npm install
claude mcp add grasshopper-bridge node "$(pwd)/hybrid-server.js"
```

**Then restart Claude Code and try:**
- *"Get my current Grasshopper canvas"*
- *"Using MCP server, help me understand currently opened Grasshopper definition"*

### 3. Test & Verify Setup

🧪 **Comprehensive Testing Suite Available**

Run the automated test suite to verify all Python component creation methods:

```bash
cd scripts/testing
python test_script.py
```

**📋 For detailed testing instructions and troubleshooting:** [**PYTHON_COMPONENT_TESTING.md**](./PYTHON_COMPONENT_TESTING.md)

The test suite validates:
- ✅ Python component creation using proven ScriptVariableParam API
- ✅ Custom inputs/outputs with automatic connections
- ✅ Robust method overload handling and error recovery
- ✅ Connection verification through canvas analysis

## Features ✨

- **🔌 WebSocket API** - Real-time communication with Grasshopper (`ws://localhost:8181/live`)
- **📊 Canvas Analysis** - Get complete pseudocode representation of your Grasshopper definition
- **🐍 Python Component Creation** - Proven method for programmatic Python component creation with custom I/O using ScriptVariableParam
- **🔗 Automatic Connections** - Smart component wiring and parameter management
- **🤖 MCP Integration** - Perfect for AI agents to understand and modify your canvas

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
- **⚙️ Automation** - Programmatically create and modify Grasshopper definitions with custom parameters
- **⚡ Live Coding** - Real-time Python component development with instant connections
- **🔗 External Tools** - Connect Grasshopper with other applications and workflows
- **🧪 Testing & Validation** - Comprehensive test suite for development workflows

## Development

Each component has its own README with detailed setup and usage instructions. Start with the grasshopper-plugin to build the core component, then proceed to mcp-server for AI integration.

### Component Dependencies

1. **grasshopper-plugin** - Independent (requires Rhino/Grasshopper)
2. **mcp-server** - Depends on grasshopper-plugin WebSocket server
3. **scripts** - Depends on grasshopper-plugin WebSocket server

---

*Happy coding with AI! 🎉*