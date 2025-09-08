# MCP Server Guide 🚀

This guide explains how to set up and use the MCP (Model Context Protocol) server, which acts as a bridge between your Grasshopper plugin and external tools like Claude Code and VSCode extensions.

## 1. Architecture Overview

The MCP server is a **hybrid server** that provides two interfaces:

1. **MCP Protocol (stdio)** - For AI agents like Claude Code
2. **HTTP REST API** - For VSCode extensions and web clients

Both interfaces share the same WebSocket connection to Grasshopper and maintain synchronized state.

```
Claude Code ←→ MCP Protocol (stdio) ←→┐
                                      ├→ Shared State → Grasshopper WebSocket
VSCode/Web  ←→ HTTP API (port 3001) ←→┘                  (ws://localhost:8181)
```

## 2. Installation & Setup

### Prerequisites

- [Node.js](https://nodejs.org/) v18+ installed
- Rhino/Grasshopper with your WebSocket component running on port 8181
- Claude Code desktop app (for MCP integration)

### Installation Steps

1. **Clone or navigate to the MCP server directory:**
   ```bash
   cd /Users/Joo/01_Projects/grasshopper_live_bridge/mcp_server
   ```

2. **Install dependencies:**
   ```bash
   npm install
   ```

3. **Test the connection to Grasshopper:**
   - Make sure Rhino and Grasshopper are running
   - Ensure your WebSocket component is on the canvas
   - The component should be listening on `ws://localhost:8181/live`

## 3. Configuring Claude Code (Important: Do NOT Start the Server Manually!)

⚠️ **IMPORTANT**: When using the MCP server with Claude Code, you should **NOT** start the server manually using `npm run` commands. Claude Code will automatically start and manage the server process when you configure it properly.

## 4. Adding the MCP Server to Claude Code

### Recommended: Using Claude Code CLI (Easiest Method)

The simplest way to add the MCP server is using Claude Code's built-in commands:

#### Step 1: Open a terminal in this directory and run:

**For global access (works in any project):**
```bash
cd mcp_server
claude mcp add grasshopper-bridge node "$(pwd)/hybrid-server.js"
```

**For project-specific access:**
```bash
cd mcp_server  
claude mcp add --scope project grasshopper-bridge node "$(pwd)/hybrid-server.js"
```

**With verbose logging:**
```bash
cd mcp_server
claude mcp add --env MCP_LOG_LEVEL=verbose grasshopper-bridge node "$(pwd)/hybrid-server.js"
```

#### Step 2: Verify installation
```bash
claude mcp list
claude mcp get grasshopper-bridge
```

#### Step 3: Test in Claude Code
Before you test it with Claude Code, stop Claude Code (Ctrl+C) and start Claude Code again.
In Claude Code, use the `/mcp` command to check server status, then test with:
- "Use the hello_world MCP tool"
- "Get the current Grasshopper canvas state"
- "List available MCP tools"

### Alternative: Manual Configuration (Advanced Users)

If you prefer manual configuration, you can edit the Claude desktop config:

**Config file locations:**
- **macOS**: `~/.config/claude-desktop/claude_desktop_config.json`
- **Windows**: `%APPDATA%\claude-desktop\claude_desktop_config.json`  
- **Linux**: `~/.config/claude-desktop/claude_desktop_config.json`

**Add this to the config:**
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

**Important:** Use the full absolute path to `hybrid-server.js` in your project directory.

## 5. Available MCP Tools

### Canvas Tools
- `get_canvas_state` - Get the current state of the Grasshopper canvas
- `get_selection` - Get currently selected components
- `query_canvas_json` - Query canvas data with JSONPath expressions
- `get_component_info` - Get detailed info about a specific component
- `get_canvas_statistics` - Get statistics about the canvas
- `find_components` - Find components by name, type, or error status

### Script Management Tools
- `create_script_file` - Create a new script file for a component
- `push_script_update` - Push script changes to Grasshopper
- `list_scripts` - List all script files in the project
- `confirm_last_update` - Verify script was applied
- `delete_script_file` - Delete a script file

### Testing Tools
- `hello_world` - Test the connection
- `get_recent_logs` - View recent event logs

## 6. HTTP API Endpoints

For VSCode extensions and web clients:

### Status & Info
- `GET /` - Server info and available endpoints
- `GET /api/status` - Server and connection status

### Canvas Operations
- `GET /api/canvas` - Get canvas state
  - Query params: `includeSelection=true`, `forceRefresh=true`
- `GET /api/selection` - Get current selection
- `POST /api/canvas/query` - Query canvas with JSONPath
- `GET /api/canvas/statistics` - Canvas statistics
- `GET /api/components/:uuid` - Get component by UUID

### Script Management
- `GET /api/scripts` - List all scripts
  - Query param: `includeContent=true`
- `POST /api/scripts/create` - Create new script
  - Body: `{ componentUuid, language, nameHint }`
- `POST /api/scripts/push` - Push script to Grasshopper
  - Body: `{ componentUuid, filePath, language }`
- `DELETE /api/scripts/:uuid` - Delete script

### Events & Monitoring
- `GET /api/events` - Get event log
  - Query params: `since=timestamp`, `kind=EVENT_TYPE`
- `GET /api/snapshots` - List canvas snapshots
- `GET /api/snapshots/:id` - Get specific snapshot

## 7. Project Structure

```
mcp_server/
├── hybrid-server.js         # Main entry point (runs both servers)
├── src/
│   ├── servers/
│   │   ├── mcp.js          # MCP protocol server
│   │   └── http.js         # HTTP REST API server
│   ├── websocket-client.js # Grasshopper WebSocket client
│   ├── state/
│   │   ├── store.js        # Shared state management
│   │   └── canvas-cache.js # Canvas data caching
│   ├── tools/
│   │   ├── canvas.js       # Canvas inspection tools
│   │   └── scripts.js      # Script management tools
│   └── utils/
│       ├── logger.js       # Logging utility
│       ├── json-query.js   # JSON querying
│       └── file-system.js  # File operations
```

## 8. Development Tips

### Testing with curl

Test HTTP endpoints:
```bash
# Get server status
curl http://localhost:3001/api/status

# Get canvas state
curl http://localhost:3001/api/canvas

# Create a script file
curl -X POST http://localhost:3001/api/scripts/create \
  -H "Content-Type: application/json" \
  -d '{"componentUuid": "abc-123", "language": "python"}'
```

### Monitoring WebSocket Connection

The server automatically reconnects to Grasshopper if the connection is lost. Check the logs to monitor connection status:

```bash
# Run with verbose logging
npm run hybrid:verbose
```

### State Management

The server maintains:
- Canvas snapshots (last 10)
- Event log (last 1000 events)
- Script file mappings
- Cache with TTL (15 seconds default)

## 9. Manual Server Startup (For HTTP API / VSCode Development Only!)

⚠️ **Only use these commands if you're developing VSCode extensions or need the HTTP API independently of Claude Code:**

### Option 1: Hybrid Mode (Both MCP and HTTP)
```bash
npm run hybrid
```

With verbose logging:
```bash
npm run hybrid:verbose
```

### Option 2: HTTP-Only Mode
For VSCode extension development:
```bash
npm start
```

### Option 3: MCP-Only Mode (Debugging only)
```bash
npm run mcp
```

**Remember**: If you start the server manually and then try to use it with Claude Code, you'll get port conflicts! Kill the manual process first with `Ctrl+C` before using Claude Code.

## 10. Troubleshooting

### MCP server not appearing in Claude Code

1. **Check config syntax:** Ensure the JSON is valid
2. **Check file path:** Use absolute paths in the config
3. **Check Node.js:** Ensure `node` is in your PATH
4. **Kill any manual server processes:** If you previously started the server manually:
   ```bash
   # Windows
   taskkill /f /im node.exe
   # macOS/Linux
   killall node
   ```
5. **Check logs:** Only for debugging - don't leave this running:
   ```bash
   node /path/to/hybrid-server.js
   ```

### Cannot connect to Grasshopper

1. **Check Grasshopper is running** with the WebSocket component on canvas
2. **Check port 8181** is not blocked:
   ```bash
   lsof -i :8181
   ```
3. **Check WebSocket URL** in the component matches `ws://localhost:8181/live`

### HTTP API not responding (VSCode Development Only)

1. **Check port 3000** is available:
   ```bash
   # Windows
   netstat -ano | findstr :3000
   # macOS/Linux
   lsof -i :3000
   ```
2. **Kill existing processes:**
   ```bash
   # Windows
   taskkill /PID <process_id> /F
   # macOS/Linux
   lsof -ti:3000 | xargs kill -9
   ```

## 10. Advanced Configuration

### Custom Ports

Run with custom HTTP port:
```bash
node hybrid-server.js --port 4000
```

### Logging Levels

- `--quiet` - Minimal logging
- `--verbose` - Detailed logging

### Environment Variables

You can also configure via environment variables:
```bash
export MCP_HTTP_PORT=4000
export MCP_LOG_LEVEL=verbose
npm run hybrid
```

## 11. Integration Examples

### VSCode Extension

```javascript
// In your VSCode extension
const response = await fetch('http://localhost:3001/api/canvas');
const canvas = await response.json();
console.log(`Canvas has ${canvas.data.Components.length} components`);
```

### Claude Code Usage

In Claude Code, simply ask:
- "Get the current Grasshopper canvas and analyze the component types"
- "Create a Python script for component with UUID abc-123"
- "Find all components with errors in the canvas"

The AI will automatically use the appropriate MCP tools.

## Support

For issues or questions:
1. Check the logs with `--verbose` flag
2. Ensure all prerequisites are met
3. Try running each server mode independently to isolate issues

Happy coding! 🎉