# 🚀 Grasshopper AI Bridge

Connect your Grasshopper definitions to AI assistants like Claude Code! This bridge lets AI understand and help with your parametric designs.

## 🎯 What This Does

This tool creates a smart connection between:
- **Your Grasshopper definitions** (the visual programming you do in Rhino)
- **AI assistants** (like Claude Code) that can understand and help with your designs
- **Web tools** for developers who want to build extensions

Think of it as a **translator** that helps AI understand what you're building in Grasshopper!

## 🏃‍♂️ Quick Start

### ✅ What You Need
- **Rhino with Grasshopper** running
- **Node.js** installed on your computer ([Download here](https://nodejs.org/))
- **Claude Code desktop app** ([Get it here](https://claude.ai/))

### 🚀 Setup (3 Steps)

**1. Install the bridge**
```bash
cd mcp-server
npm install
```

**2. Connect to Claude Code**
```bash
claude mcp add grasshopper-bridge node "$(pwd)/hybrid-server.js"
```

**3. Test it works**
Open Claude Code and try saying:
- *"Get my current Grasshopper canvas"*
- *"What components do I have in my definition?"*
- *"Help me understand what this Grasshopper file does"*

## 🎉 What You Can Do

Once connected, you can ask Claude Code to:

### 📊 **Understand Your Designs**
- *"Explain what this Grasshopper definition does"*
- *"What's the main workflow in my current canvas?"*
- *"How many components do I have?"*

### 🔍 **Find Things**
- *"Find all the offset components"*
- *"Show me components with errors"*
- *"What sliders control my design?"*

### 🛠️ **Analyze Your Workflow**
- *"How can I optimize this definition?"*
- *"What's the data flow in this design?"*
- *"Suggest improvements for better performance"*

### 🎯 **Get Specific Info**
- *"What's connected to this component?"*
- *"Show me the selected components"*
- *"Give me statistics about my canvas"*
- *"Does this component have any errors or warnings?"*
- *"Show me all runtime messages for the selected component"*

## 🆘 Troubleshooting

### ❌ "Cannot connect to Grasshopper"
1. **Make sure Grasshopper is open** in Rhino
2. **Check the plugin is loaded** - you should see a "Live Coding Controller" component
3. **Place the component** on your canvas (it creates the connection)

### ❌ "MCP Server not working"
1. **Restart Claude Code** after setup
2. **Check the path** in your MCP configuration is correct
3. **Try the test**: `claude mcp list` should show "grasshopper-bridge"

### ❌ "Commands not working"
1. **Use natural language** - say "get my canvas" instead of technical commands
2. **Make sure Grasshopper is active** with a definition open
3. **Check the Live Coding component** is on your canvas

## 🎨 Examples

Here are some things you can try once it's working:

### For Designers
```
"Analyze my current Grasshopper definition"
"What does this design do?"
"How can I improve this workflow?"
"Find components that might be causing problems"
```

### For Advanced Users
```
"Help me optimize this definition for performance"
"Show me the data flow in this definition"
"Analyze the algorithmic complexity of this workflow"
"Find potential bottlenecks in my definition"
```

---

## 🔧 Technical Details

<details>
<summary>Click to expand technical information</summary>

### Architecture
The bridge runs as a hybrid server providing:
- **MCP Protocol** (stdio) for AI agents like Claude Code
- **HTTP REST API** (port 3000) for web applications and extensions
- **WebSocket Client** connecting to Grasshopper plugin at `ws://localhost:8181/live`

### Available Tools
The bridge provides 5 focused MCP tools for AI interaction:

**Canvas Analysis (5 tools)**
- `get_canvas_state` - Get complete canvas pseudocode
- `get_selection` - Get currently selected components
- `query_canvas_pseudocode` - Search canvas with text/regex
- `get_component_info` - Get details about specific components including runtime messages (errors, warnings, remarks)
- `find_components` - Search components by name/type

### HTTP API Endpoints
For developers building extensions:

- `GET /api/canvas` - Get canvas state
- `GET /api/selection` - Get current selection
- `POST /api/canvas/query` - Query canvas data
- `GET /api/components/:uuid` - Get component details
- `GET /api/events` - Get event log
- `GET /api/snapshots` - Get canvas history

### Caching System
The bridge includes intelligent caching:
- **15-second TTL** for canvas data
- **Automatic fallback** to snapshots when Grasshopper unavailable
- **Memory management** with automatic cleanup
- **Component extraction** from pseudocode for fast lookups

### Manual Development
For developers working on the bridge itself:

```bash
# Start the server manually
npm start

# Start with verbose logging
npm run start:verbose

# Test connection
curl http://localhost:3000/api/status
```

### Configuration
The server can be configured via command line:
```bash
node hybrid-server.js --port 3000 --verbose
```

### Project Structure
```
mcp-server/
├── hybrid-server.js         # Main entry point
├── package.json            # Dependencies and scripts
├── src/
│   ├── servers/
│   │   ├── mcp.js          # MCP protocol implementation
│   │   └── http.js         # HTTP REST API
│   ├── tools/              # MCP tool implementations
│   │   ├── canvas.js       # Canvas analysis tools
│   │   └── scripts.js      # Script management tools
│   ├── state/              # State management & caching
│   │   ├── canvas-cache.js # Smart canvas data cache with TTL
│   │   └── store.js        # Central state store with snapshots
│   ├── websocket-client.js # Grasshopper WebSocket client
│   └── utils/              # Utilities and helpers
└── README.md               # This file
```

</details>