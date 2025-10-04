# 🎯 Grasshopper Live Bridge - Development TODO

This document tracks the development roadmap for enhanced Claude Code integration with Grasshopper through the MCP bridge and plugin capabilities.


### [✅] Understand Custom Input/Output Specification Format
**Goal:** Learn how to specify custom inputs/outputs to Python script components when it's created programmatically.

### [✅] Learn Component Connection Mechanisms
**Goal:** Understand how to connect Python components to existing components in Grasshopper.

**Solution:** Fixed type casting issue where sliders (`IGH_Param`) weren't being found by component search (`IGH_Component`). Implemented multi-strategy search supporting both component and parameter objects, with robust UUID and nickname-based connection resolution.

**Key Fixes:**
- Enhanced `MakeConnections()` to handle both `IGH_Component` and `IGH_Param` objects
- Fixed timing issue by making connections immediately after component creation (outside solution context)
- Added 5-strategy search: full GUID, formatted GUID, component nickname, parameter nickname, partial UUID
- Validated working connections between sliders and Python components

### [✅] Test Python Component Creation Method in Rhino 8

**Goal:** ~~Verify which of the three Python component creation methods actually works~~ **UPDATED:** Single proven method validated and working.

**Proven Method:**
1. **`create_python_component`** - Uses ScriptVariableParam API with RhinoCodePluginGH.Components.Python3Component

**Testing Checklist:**
- [✅] Test proven method in current Rhino 8 environment
- [✅] Verify custom input/output parameter creation works with ScriptVariableParam
- [✅] Document working endpoint for Claude Code integration

**Outcome:** ✅ **`create_python_component` endpoint validated and working reliably for Python component creation with custom I/O and connections.**


### [✅] Create MCP Tool for Python Script Creation
**Goal:** Enable Claude Code to execute "convert my script to python code" commands.

**What it does:**
- Creates Python components on Grasshopper canvas programmatically
- Supports custom inputs/outputs and automatic connections
- Uses proven `create_python_component` endpoint with ScriptVariableParam API

**Implementation Plan:**
- **Tool Name:** `mcp__grasshopper-bridge__create_script_component`
- **Location:** Add to `mcp-server/src/tools/canvas.js`
- **Payload:** `{ x, y, code, inputs[], outputs[], connections[] }`
- **Returns:** Component GUID and success status


## Pseudocode Format Migration

### [✅] Switch to Enhanced Pipe-Delimited with Types Format
**Goal:** Migrate entire codebase to use the new Enhanced Pipe-Delimited with Types pseudocode format.

**New Format:**
```
variable|x,y|comp_uuid: ComponentType = "Component Name" | ["Input Name"(InputType):param_uuid, "_Unused Input"(Type):param_uuid] | ["Output Name"(OutputType):param_uuid]
```

**Real Examples with Built-in Components:**
```
move_transform|200,150|a1b2c3d4: Geometry = "Move" | ["Geometry"(Geometry):input1_uuid, "Motion"(Vector3d):input2_uuid] | ["Geometry"(Geometry):output1_uuid, "Transform"(Transform):output2_uuid]

circle_cnr|300,200|e5f6g7h8: Curve = "Circle CNR" | ["Center"(Point3d):input1_uuid, "_Normal"(Vector3d):input2_uuid, "Radius"(Number):input3_uuid] | ["Circle"(Curve):output1_uuid]

explode_curve|150,300|i9j0k1l2: List[Curve] = "Explode" | ["Curve"(Curve):input1_uuid] | ["Segments"(List[Curve]):output1_uuid, "Vertices"(List[Point3d]):output2_uuid]
```

**Implementation Locations:**
- `grasshopper-plugin/LiveCodingGH/LiveCodingComponent.cs` - GetCanvasInfo() method (~line 309)
- Update `PseudocodeComponent`, `PseudocodeInput`, `PseudocodeOutput` classes to include:
  - Component x,y position
  - Parameter type information
  - Parameter UUIDs
- `mcp-server/src/tools/canvas.js` - All parsing functions
- Update all MCP tools to understand new format

**Benefits for Claude Code:**
- Complete type information for connection compatibility
- Position data for spatial understanding
- Unused parameter visibility for connection possibilities
- Exact UUIDs for wire management operations
- Component type recognition (built-in vs custom clusters)


### [✅] Complete Input/Output Notation Enhancement
**Goal:** Include all inputs and outputs in component notation, not only used ones, to help Claude understand component capabilities. ✅ **COMPLETED**

**Implementation:**
- ✅ All inputs and outputs shown in pseudocode (regardless of connection status)
- ✅ UUIDs included for all parameters for precise wire management
- ✅ Type information included for all parameters
- ✅ No visual distinction between connected/unconnected (user preference)
- ✅ Full component capabilities visible to Claude

**Changes Made:**
- Removed underscore prefix from unconnected inputs (user feedback: "it does not matter much")
- Updated documentation in CLAUDE.md
- Updated code comments to reflect current format
- MCP parsing maintains `isUnused` flag for backward compatibility (always false now)

### [✅] Wire Connection/Disconnection MCP Tool
**Goal:** Create a separate MCP tool to connect or disconnect wires between components in Grasshopper. ✅ **COMPLETED**

**Implementation:**
- ✅ **MCP Tool:** `manage_wire_connections`
- ✅ **C# Endpoint:** `manage_wires` with three helper methods
  - `ConnectWire` - Connects wires using `AddSource()`
  - `DisconnectWire` - Disconnects wires using `RemoveSource()`
  - `ExecutePartialOperation` - Bulk operations using `RemoveAllSources()`
- ✅ **Actions supported:** `connect`, `disconnect`
- ✅ **UUID validation:** 36-char hyphenated format
- ✅ **Handles both:** `IGH_Component` and `IGH_Param` objects
- ✅ **Error reporting:** Returns success/failure counts with detailed errors
- ✅ **Auto-refresh:** Triggers solution update after wire changes

**Files Modified:**
- `grasshopper-plugin/LiveCodingGH/LiveCodingComponent.cs` - Added manage_wires endpoint
- `mcp-server/src/servers/mcp.js` - Added tool schema and case handler
- `mcp-server/src/tools/canvas.js` - Added manageWireConnections function
- `CLAUDE.md` - Updated documentation with new action and MCP tool

**Payload Structure:**
```json
{
  "action": "connect|disconnect",
  "connections": [
    {
      "sourceComponentUuid": "component-uuid",
      "sourceOutputIndex": 0,
      "targetComponentUuid": "component-uuid",
      "targetInputIndex": 0
    }
  ],
  "partialOperations": [
    {
      "componentUuid": "component-uuid",
      "parameterType": "input|output",
      "parameterIndex": 0,
      "operation": "disconnect_all"
    }
  ]
}
```



### [ ] Component Creation via Pseudocode Notation
**Goal:** Allow Claude to create single or multiple components using the same pseudocode notation format for consistency.

**What it does:**
- Parse pseudocode-style component definitions
- Create components automatically with specified inputs/outputs
- Auto-wire components to each other or existing components as specified
- Support batch component creation for complex workflows

**Design Considerations:**
- Use same notation as pseudocode output for consistency
- Support position specifications in the notation
- Enable automatic connection resolution based on variable names
- Handle component placement intelligently (avoid overlaps)

**Enhanced Tool Plan:**
- **Tool Name:** `mcp__grasshopper-bridge__create_components_from_pseudocode`
- **Input:** Pseudocode-style component definitions
- **Processing:** Parse notation → Create components → Wire automatically
- **Output:** Created component UUIDs and success status

**Example Usage:**
```
# Claude could specify components like this:
new_slider@(100,200): Number = Number Slider(min=0, max=100, value=50)
processor@(300,200): Geometry = Transform(geometry=new_slider.output, transform=identity_transform)
```

**Implementation Requirements:**
- Extend existing `createScriptComponent` functionality
- Add pseudocode parsing logic in `mcp-server/src/tools/canvas.js`
- Support intelligent component positioning (auto-layout or specified positions)
- Handle component dependencies and connection order
- Validate pseudocode syntax before component creation

### [ ] Add Group Support to Pseudocode Notation
**Goal:** Enable Claude to fetch information about groups of components in pseudocode notation.


### [ ] Add Component Context Retrieval Tool

**Goal:** Help Claude understand component relationships when analyzing specific parts of a definition.

**What it does:**
- Retrieves contextual information around a specific component
- Configurable depth (N nodes left/right in dependency graph)
- Returns upstream/downstream connected components with details

**Implementation Plan:**
- **Tool Name:** `mcp__grasshopper-bridge__get_component_context`
- **New Endpoint:** `get_component_context` in Grasshopper plugin
- **Payload:** `{ componentId, depth?, includeDataPreviews? }`
- **Response:** `{ centerComponent, upstreamComponents[], downstreamComponents[], connections[] }`


### [✅] Enable Dynamic Data Preview Control

**Current State:** Data preview functionality is now dynamically controllable via MCP tool parameters.

**Goal:** Allow Claude to toggle data previews without rebuilding the plugin. ✅ **COMPLETED**

**Implementation:**
- Added optional parameters to existing `get_canvas_state` MCP tool:
  - `includeDataPreviews` (boolean, default: false) - Enable/disable inline data previews
  - `maxPreviewLength` (number, default: 20) - Control preview truncation length
- Preview format uses **Option 1 syntax**: `"Name"(Type):uuid="preview"`
- Applied to **outputs only** to avoid data duplication
- Backward compatible - defaults maintain current behavior

**Updated Files:**
- `grasshopper-plugin/LiveCodingGH/LiveCodingComponent.cs` - GetCanvasInfo method with preview parameters
- `mcp-server/src/servers/mcp.js` - Tool schema with new parameters
- `mcp-server/src/tools/canvas.js` - Parameter parsing and regex updates
- `mcp-server/src/state/canvas-cache.js` - Parameter passthrough
- `mcp-server/src/websocket-client.js` - WebSocket payload updates
- `scripts/testing/*.py` - Test scripts updated to use 30-char previews

**Example Usage:**
```javascript
await getCanvasState({ includeDataPreviews: true, maxPreviewLength: 30 })
```

**Output Format:**
```
circles|100,200|uuid: Circle = "Circles" | ["Points"(Point):uuid] | ["Circles"(Circle):uuid="[Circle(r=5.0), Circle(r=.."]
```


### [ ]  Canvas State Diffing
**Goal:** Compare canvas states before/after operations to track changes.

**Tool Name:** `mcp__grasshopper-bridge__canvas_diff`

