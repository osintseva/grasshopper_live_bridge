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
- Supports actions: `ping`, `create_slider`, `create_python_component`, `update_script`, `get_canvas_info`, `get_selection`, `get_component_info`, `manage_wires`
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
- All inputs and outputs shown regardless of connection status
- Standard hyphenated GUIDs (36 chars): `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`
- Topologically sorted for dependency analysis

## Critical Implementation Details

### Python Component Creation
- Uses RhinoCode API (Rhino 8): `RhinoCodePluginGH.Components.Python3Component`
- Custom I/O via `ScriptVariableParam` (not legacy GhPython)
- Method overload handling: tries `Create(string, string)` → `Create(string)` → `Create(string, Bitmap, bool)`
- **IMPORTANT**: Default parameters (x, y, out, a) must be removed before adding custom ones
- Call `VariableParameterMaintenance()` after adding custom parameters
- **Recommended Workflow**:
  1. Create component with `create_script_component`
  2. Connect inputs with `manage_wire_connections` (do this automatically unless user specifies otherwise)
  3. **ALWAYS** check for errors with `get_component_info` to see runtime messages
  4. If errors/warnings found, fix the code and recreate the component
- **Outputting Lists/Trees**: To properly output lists or trees from Python components, use DataTree:
  ```python
  import Grasshopper as gh

  # For list outputs
  output_var = gh.DataTree[object]()
  output_var.AddRange(list_data, gh.Kernel.Data.GH_Path(0))

  # Single values can be assigned directly
  single_output = value
  ```

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
The `MakeConnections` method uses 5 fallback strategies:
1. Full GUID matching (36-char hyphenated)
2. GUID without hyphens (32-char, reformatted to 36-char)
3. Nickname matching (components)
4. Nickname matching (parameters/sliders)
5. Partial UUID matching (prefix match, 36-char hyphenated)

### Canvas Analysis
- `GetCanvasInfo()` generates pseudocode representation
- Topological sort ensures dependency order
- Variable names generated from NickName → Name → ComponentType
- **UUID Format Standard**: All UUIDs use 36-character hyphenated lowercase format: `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`
  - C# uses `Guid.ToString()` (default format)
  - JavaScript normalizes to lowercase for comparison
  - 32-char format supported only in fallback matching (strategy 2)

## MCP Tools Available

### Canvas Tools (`mcp-server/src/tools/canvas.js`)
- `get_canvas_state` - Full canvas pseudocode
- `get_selection` - Currently selected components (full UUIDs)
- `query_canvas_pseudocode` - Text/regex/wildcard search
- `get_component_info` - Detailed component data by UUID including runtime messages (errors, warnings, remarks)
- `find_components` - Search by name/type/error status
- `create_script_component` - Create Python components with custom I/O (no automatic wiring)
- `manage_wire_connections` - Connect/disconnect wires between components (use this after creating components)
- `search_rhinocommon_docs` - Semantic/keyword search through RhinoCommon API documentation

### Dynamic Agent Persona System (`mcp-server/src/tools/agent-persona.js`)

AI agents dynamically construct their system prompts by loading specialized roles, workflow recipes, and reference docs on-demand using pseudo-XML formatted content from `misc/agent-persona/`.

**Three Tools:**
- `get_role({ key })` - Load agent identity/expertise returning `<identity>`, `<capabilities>`, `<recipe_selection>` sections
- `get_recipe({ key })` - Load step-by-step workflow checklist returning `<overview>`, `<prerequisites>`, `<steps>` sections
- `get_doc({ key })` - Load reference documentation with domain-specific semantic tags

**Subagent Pattern:** Spawned subagents call `get_role()` first to assume specialized expertise (e.g., "gh-architect"), then follow linked recipes and load docs as needed - building their own system prompt for autonomous task execution.

## Development Patterns

### Error Handling
- Grasshopper plugin logs to Debug Log output (2000 entry limit)
- MCP server has 3 log levels: quiet, normal, verbose
- WebSocket messages capped at 10MB (truncated with error if exceeded)

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
