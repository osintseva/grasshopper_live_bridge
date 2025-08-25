# 🦗 Grasshopper MCP Server

## What is this?

Think of this as a **smart bridge** that connects AI tools like Claude Code directly to your Grasshopper canvas. Instead of manually describing what's on your canvas, the AI can now "see" and interact with your Grasshopper definition automatically!

### In Simple Terms:
- 🤖 **AI Integration**: Claude Code can read your Grasshopper canvas and help you build
- 🔄 **Live Connection**: Real-time updates when you change components 
- 📝 **Script Management**: Create and edit Python/C# scripts directly from AI chat
- 🎯 **Smart Analysis**: AI can understand your definition and suggest improvements
- 🛠️ **Developer Tools**: HTTP API for VSCode extensions and web apps

## Why Use This?

**Before**: "Hey Claude, here's a screenshot of my Grasshopper definition..."
**After**: "Hey Claude, analyze my current canvas and optimize the data flow" ✨

This server bridges AI agents and development tools with Grasshopper's visual programming environment using both MCP (Model Context Protocol) and HTTP APIs.

## Features

- **Dual Protocol Support**: MCP for AI agents (Claude Code) and HTTP REST API for traditional tools (e.g. a VScode plugin)
- **Real-time WebSocket Connection**: Direct communication with Grasshopper components
- **State Management**: Shared state between protocols with intelligent caching
- **Script Management**: Create, edit, and push scripts to Grasshopper components
- **Canvas Inspection**: Query and analyze Grasshopper canvas state
- **Event Logging**: Track all operations and changes

## 🚀 Super Quick Start

### Option 1: Automatic (Recommended)
1. Open this project in Claude Code
2. Type `/mcp` to activate 
3. Ask: "Get my current Grasshopper canvas state"
4. ✨ That's it! The server starts automatically.

### Option 2: Manual
```bash
# Install dependencies
npm install

# Run the hybrid server
node hybrid-server.js

# The server is now available at:
# - MCP: stdio (for Claude Code)
# - HTTP: http://localhost:3001 (for VSCode/web)
```

### Prerequisites
- ✅ Node.js v18+ installed
- ✅ Grasshopper running with WebSocket component on canvas
- ✅ WebSocket component listening on port 8181

## 🤖 Claude Code Integration

### ✅ Already Configured!
This project includes a `.mcp.json` file that automatically sets up Claude Code integration. No manual configuration needed!

**Just use `/mcp` in Claude Code and start asking questions like:**
- "What components are on my Grasshopper canvas?"
- "Create a Python script for the selected component"
- "Find all components that have errors"
- "Analyze the data flow in my definition"

## API Examples

### HTTP API

```bash
# Get canvas state
curl http://localhost:3001/api/canvas

# Get current selection
curl http://localhost:3001/api/selection

# Create a script file
curl -X POST http://localhost:3001/api/scripts/create \
  -H "Content-Type: application/json" \
  -d '{"componentUuid": "abc-123", "language": "python"}'
```

### 💬 MCP Tools (in Claude Code)

Ask Claude natural language questions:
- "Get the current Grasshopper canvas state"
- "Create a Python script for component abc-123"
- "Find all components with errors"
- "What's the purpose of this Grasshopper definition?"
- "How can I optimize the performance of this canvas?"
- "Generate documentation for this definition"

## Architecture

```
┌─────────────┐     ┌─────────────┐
│ Claude Code │     │   VSCode    │
└──────┬──────┘     └──────┬──────┘
       │ MCP               │ HTTP
       ↓                   ↓
┌──────────────────────────────────┐
│        Hybrid MCP Server         │
│  ┌────────────────────────────┐  │
│  │     Shared State Store     │  │
│  └────────────────────────────┘  │
│  ┌────────────────────────────┐  │
│  │   WebSocket Client (ws)    │  │
│  └────────────────────────────┘  │
└─────────────┬────────────────────┘
              │ WebSocket
              ↓
       ┌──────────────┐
       │ Grasshopper  │
       └──────────────┘
```

## Available Tools

### Canvas Operations
- `get_canvas_state` - Full canvas JSON
- `get_selection` - Selected components
- `query_canvas_json` - JSONPath queries
- `get_component_info` - Component details
- `find_components` - Search components

### Script Management
- `create_script_file` - New script files
- `push_script_update` - Send to Grasshopper
- `list_scripts` - Project scripts
- `delete_script_file` - Remove scripts

### Monitoring
- `get_recent_logs` - Event history
- `hello_world` - Connection test

## 🔧 Development & Testing

📖 **Want to extend the server?** See **[DEVELOPMENT_GUIDES.md](./DEVELOPMENT_GUIDES.md)** for step-by-step guides on adding tools, prompts, and resources.

```bash
# Run with verbose logging
node hybrid-server.js --verbose

# Run with custom port
node hybrid-server.js --port 3002

# Run HTTP-only (for testing the API)
npm start

# Test connection to Grasshopper
curl http://localhost:3001/api/status
```

### 🧪 Testing Your Setup
1. **Test WebSocket connection**: Ensure Grasshopper shows "Connected" in component
2. **Test HTTP API**: Visit `http://localhost:3001/api/status`
3. **Test MCP Tools**: Ask Claude to "get canvas state"

## Project Structure

```
mcp_server/
├── hybrid-server.js     # Main entry point
├── src/
│   ├── servers/        # Protocol implementations
│   ├── tools/          # Business logic
│   ├── state/          # State management
│   ├── utils/          # Utilities
│   └── websocket-client.js
└── package.json
```

## 📋 Requirements

### Essential
- 🟢 **Node.js v18+** - [Download here](https://nodejs.org/)
- 🟢 **Rhino 7/8** with Grasshopper running
- 🟢 **WebSocket component** on your Grasshopper canvas (port 8181)

### For AI Features
- 🟡 **Claude Code** - [Download here](https://claude.ai/code)

### For Development
- 🔵 **VSCode** (optional) - for extension development
- 🔵 **curl** or **Postman** - for API testing


## 🆘 Need Help?

### Quick Troubleshooting
- 🔴 **Server won't start**: Check if port 3001 is free with `lsof -i :3001`
- 🔴 **Can't connect to Grasshopper**: Make sure WebSocket component is on canvas and listening on port 8181
- 🔴 **MCP not working**: Try `/mcp` command in Claude Code to activate

### Documentation
- 📚 **[MCP Start Guide.md](./MCP%20Start%20Guide.md)** - Comprehensive setup guide
- 🐛 **Issues**: Check logs with `node hybrid-server.js --verbose`
- 💬 **Questions**: Create an issue on GitHub

---

## 🎉 What's Next?

Once you're set up, try these awesome workflows:
1. **AI-Powered Analysis**: "Claude, what does this Grasshopper definition do?"
2. **Smart Script Generation**: "Create a Python script that generates a spiral"
3. **Performance Optimization**: "Find bottlenecks in my current definition"
4. **Documentation Generation**: "Create technical documentation for this canvas"

Happy building! 🚀