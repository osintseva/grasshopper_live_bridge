# MCP Server - Grasshopper Bridge 🚀

Node.js MCP (Model Context Protocol) server that bridges Grasshopper to Claude Code and provides HTTP API for external tools.

## Prerequisites

- **Node.js v18+** installed
- **Grasshopper plugin** running (see `../grasshopper-plugin/README.md`)
- **Claude Code desktop app** (for MCP integration)
- WebSocket component active on port 8181

## Quick Setup

All commands should be run from this directory (`mcp-server/`):

### 1. Install Dependencies

```bash
npm install
```

### 2. Add to Claude Code

```bash
# For global access
claude mcp add grasshopper-bridge node "$(pwd)/hybrid-server.js"

# Verify installation
claude mcp list
claude mcp get grasshopper-bridge
```

### 3. Test Connection

Ensure your Grasshopper plugin is running, then restart Claude Code and test with:
- "Use the hello_world MCP tool"
- "Get the current Grasshopper canvas state"
- "List available MCP tools"

## Architecture

The MCP server is a **hybrid server** providing two interfaces with intelligent caching:

1. **MCP Protocol (stdio)** - For AI agents like Claude Code
2. **HTTP REST API** - For VSCode extensions and web clients

```
Claude Code ←→ MCP Protocol (stdio) ←→┐
                                      ├→ Smart Cache → Grasshopper WebSocket
VSCode/Web  ←→ HTTP API (port 3001) ←→┘   + State Store   (ws://localhost:8181)
```

### Caching & State Management

The server includes a sophisticated caching system for optimal performance:

#### Canvas Cache (`src/state/canvas-cache.js`)
- **TTL-based caching** (15 seconds default) for canvas pseudocode
- **Intelligent fallback** - returns latest snapshot if Grasshopper is unavailable
- **Component extraction** - parses individual components from pseudocode using regex
- **Different cache strategies** for canvas vs selection data

#### State Store (`src/state/store.js`)
- **Canvas snapshots** - keeps last 10 timestamped snapshots for fallback
- **Event logging** - maintains last 1000 events for debugging
- **Script mappings** - tracks componentUuid → filePath relationships
- **Connection monitoring** - tracks Grasshopper WebSocket health
- **Auto-cleanup** - prevents memory leaks with TTL expiration

#### Example Cache Flow
```javascript
// 1st call - fresh data from Grasshopper
get_canvas_state() → WebSocket call → Cache for 15s → Return pseudocode

// 2nd call within 15s - instant response
get_canvas_state() → Return cached data (no WebSocket)

// If Grasshopper disconnects
get_canvas_state() → WebSocket fails → Return latest snapshot as fallback
```

This design ensures:
- **Fast responses** for repeated canvas queries
- **Resilience** when Grasshopper is temporarily unavailable
- **Fresh data** when the canvas actually changes
- **Memory efficiency** with automatic cleanup

## Available MCP Tools

### Canvas Analysis
- `get_canvas_state` - Complete canvas state and structure
- `get_selection` - Currently selected components
- `query_canvas_json` - JSONPath queries on canvas data
- `get_component_info` - Detailed component information
- `get_canvas_statistics` - Canvas metrics and statistics
- `find_components` - Search by name, type, or error status

### Script Management
- `create_script_file` - Create Python/C# script files
- `push_script_update` - Deploy scripts to Grasshopper
- `list_scripts` - View all project scripts
- `confirm_last_update` - Verify script deployment
- `delete_script_file` - Remove script files

### Development
- `hello_world` - Test server connection
- `get_recent_logs` - View server event logs

## HTTP API (For VSCode/Web Development)

### Status & Canvas
- `GET /api/status` - Server and connection status
- `GET /api/canvas` - Canvas state with optional selection
- `POST /api/canvas/query` - JSONPath queries
- `GET /api/components/:uuid` - Component details

### Scripts
- `GET /api/scripts` - List all scripts
- `POST /api/scripts/create` - Create new script
- `POST /api/scripts/push` - Deploy script changes
- `DELETE /api/scripts/:uuid` - Delete script

### Events
- `GET /api/events` - Event log with filtering
- `GET /api/snapshots` - Canvas history snapshots

## Manual Development Modes

⚠️ **Only for development** - Claude Code manages the server automatically when configured

### HTTP-Only (VSCode development)
```bash
npm start
```

### Hybrid Mode (Both MCP + HTTP)
```bash
npm run hybrid
```

### With Verbose Logging
```bash
npm run hybrid:verbose
```

## Project Structure

```
mcp-server/
├── hybrid-server.js         # Main entry point
├── mcp-server.js           # Pure MCP server (legacy)
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

## Troubleshooting

### MCP Server Not Working
1. **Check prerequisites**: Grasshopper plugin must be running
2. **Restart Claude Code** after adding MCP server
3. **Use absolute paths** in MCP configuration
4. **Kill manual processes** if you started server manually:
   ```bash
   # Windows
   taskkill /f /im node.exe
   # macOS/Linux
   killall node
   ```

### Cannot Connect to Grasshopper
1. **Verify plugin is active**: See `../grasshopper-plugin/README.md`
2. **Check port 8181**: Ensure no other applications are using it
3. **Test connection**: Use `../scripts/test-connection.js`

### Port Conflicts
If you get port conflicts, ensure no manual server instances are running before using Claude Code.

## Alternative Configuration (Advanced)

Instead of Claude Code CLI, you can manually edit the Claude desktop config:

**Windows**: `%APPDATA%\claude-desktop\claude_desktop_config.json`
**macOS/Linux**: `~/.config/claude-desktop/claude_desktop_config.json`

```json
{
  "mcp": {
    "tools": [
      {
        "command": ["node", "/full/path/to/hybrid-server.js"],
        "transport": {
          "kind": "stdio"
        }
      }
    ]
  }
}
```

## Next Steps

1. **Test the tools**: Try MCP tools in Claude Code
2. **Development guides**: See `DEVELOPMENT_GUIDES.md` for extending functionality
3. **HTTP API testing**: Use `curl` or Postman with the HTTP endpoints