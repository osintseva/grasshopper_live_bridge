# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

This is a monorepo providing a WebSocket-based bridge between Grasshopper (Rhino's visual programming environment) and AI assistants. The system enables real-time analysis and manipulation of Grasshopper canvases through:

1. **C# Grasshopper Plugin** - WebSocket server running inside Grasshopper
2. **Node.js MCP Server** - Hybrid MCP/HTTP server connecting AI to Grasshopper
3. **Python Testing Scripts** - Validation suite for component creation

## Build Commands

### Grasshopper Plugin (.NET)
```bash
# Build the plugin
cd grasshopper-plugin
dotnet build -c Release

# Install to Grasshopper (⚠️ close Rhino first!)
cd grasshopper-plugin/LiveCodingGH/bin/Release/net48 && cp
      LiveCodingGH.gha "$APPDATA/Grasshopper/Libraries/"
```

### MCP Server (Node.js)
```bash
cd mcp-server
npm install
npm start              # Normal mode
npm run start:verbose  # Verbose logging
```

### Testing
```bash
cd scripts/testing
python test_script.py           # Full test suite
python save_canvas_state.py     # Save canvas snapshot
```

## Architecture

### WebSocket Communication Flow
```
AI (Claude Code) ←→ MCP Server (Node.js) ←→ WebSocket (ws://localhost:8181/live) ←→ C# Plugin (Grasshopper)
```

### Key Components

**1. Grasshopper Plugin (`grasshopper-plugin/LiveCodingGH/LiveCodingComponent.cs`)**
- WebSocket server on port 8181 at `/live` endpoint
- Message pump processes commands on UI thread (50ms interval)
- Supports actions: `ping`, `create_slider`, `create_python_component`, `update_script`, `get_canvas_info`, `get_selection`
- All commands use correlation IDs for async request/response matching

**2. MCP Server (`mcp-server/`)**
- Hybrid server running both MCP (stdio) and HTTP REST API (port 3000)
- Main entry: `hybrid-server.js`
- MCP tools defined in `src/tools/canvas.js`
- Canvas caching with 15-second TTL in `src/state/canvas-cache.js`
- State management with snapshot history in `src/state/store.js`

**3. Canvas Pseudocode Format**
The system uses "Enhanced Pipe-Delimited with Types" format:
```
variable|x,y|comp_uuid: ComponentType = "Component Name" | ["Input Name"(InputType):param_uuid] | ["Output Name"(OutputType):param_uuid]
```
- Unused inputs prefixed with underscore: `"_Unused Input"`
- Standard hyphenated GUIDs (36 chars): `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`
- Topologically sorted for dependency analysis

## Critical Implementation Details

### Python Component Creation
- Uses RhinoCode API (Rhino 8): `RhinoCodePluginGH.Components.Python3Component`
- Custom I/O via `ScriptVariableParam` (not legacy GhPython)
- Method overload handling: tries `Create(string, string)` → `Create(string)` → `Create(string, Bitmap, bool)`
- **IMPORTANT**: Default parameters (x, y, out, a) must be removed before adding custom ones
- Call `VariableParameterMaintenance()` after adding custom parameters
- Connections made after component added to document (outside solution context)

### WebSocket Message Protocol
All messages use JSON with correlation IDs:
```json
{
  "action": "create_python_component",
  "correlationId": "unique-id",
  "payload": { ... }
}
```

Response format:
```json
{
  "action": "create_python_component_response",
  "correlationId": "unique-id",
  "status": "success|error|queued",
  "message": "...",
  "data": { ... }
}
```

### Connection Strategy (Component Linking)
The `MakeConnections` method uses 4 fallback strategies:
1. Full GUID matching
2. GUID without hyphens (32-char)
3. Nickname matching (components)
4. Nickname matching (parameters/sliders)

### Canvas Analysis
- `GetCanvasInfo()` generates pseudocode representation
- Topological sort ensures dependency order
- Variable names generated from NickName → Name → ComponentType
- Standard hyphenated GUIDs (36 chars) used for all component and parameter identification

## MCP Tools Available

All tools in `mcp-server/src/tools/canvas.js`:
- `get_canvas_state` - Full canvas pseudocode
- `get_selection` - Currently selected components (full UUIDs)
- `query_canvas_pseudocode` - Text/regex/wildcard search
- `get_component_info` - Detailed component data by UUID
- `find_components` - Search by name/type/error status

## Development Patterns

### Error Handling
- Grasshopper plugin logs to Debug Log output (2000 entry limit)
- MCP server has 3 log levels: quiet, normal, verbose
- WebSocket messages capped at 100KB (truncated with error if exceeded)

### State Management
- Canvas cache auto-refreshes every 15 seconds
- Snapshot history maintained for offline fallback
- Connection status tracked: 'connected' | 'disconnected' | 'error'

### Threading Model
- Grasshopper commands queued via `ConcurrentQueue<CommandMessage>`
- Timer pump processes queue on UI thread (required for document access)
- WebSocket responses sent directly to requesting session via `correlationId`

## Common Gotchas

1. **Rhino Must Be Closed** when building/installing the plugin (file lock issues)
2. **Component Must Be On Canvas** for WebSocket server to start
3. **Message Size Limits**: 100KB for WebSocket, responses truncated if exceeded
4. **Reflection API**: RhinoCode types loaded via reflection (may not exist in older Rhino versions)
5. **Async Operations**: All Grasshopper document operations must happen on UI thread via message pump

## File Locations

- Plugin builds to: `grasshopper-plugin/LiveCodingGH/bin/Release/net48/LiveCodingGH.gha`
- Install location: `%APPDATA%\Grasshopper\Libraries\`
- WebSocket endpoint: `ws://localhost:8181/live`
- HTTP API: `http://localhost:3000/api/*`
