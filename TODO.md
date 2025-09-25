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


### [ ] Complete Input/Output Notation Enhancement
**Goal:** Include all inputs and outputs in component notation, not only used ones, to help Claude understand component capabilities.

**What's needed:**
- Modify pseudocode generation to show all parameters, marking unused ones with "_" or special notation
- Include input/output UUIDs for precise wire management
- Ensure Claude can understand component I/O capabilities even when not all are used

### [ ] Wire Connection/Disconnection MCP Tool
**Goal:** Create a separate MCP tool to connect or disconnect wires between components in Grasshopper.

**What it does:**
- Connect/disconnect wires between specific component input/output UUIDs
- Support partial specification (remove all wires from a specific input/output)
- Toggle between add/remove wire operations
- Validate component existence before wire operations

**Implementation Plan:**
- **Tool Name:** `mcp__grasshopper-bridge__manage_wire_connections`
- **New C# Endpoint:** `manage_wires` in LiveCodingComponent.cs
- **Payload Structure:**
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

**Implementation Requirements:**
- Add to `mcp-server/src/tools/canvas.js`
- Add corresponding endpoint in `grasshopper-plugin/LiveCodingGH/LiveCodingComponent.cs`
- Support UUID-based component lookup (both full and short UUIDs)
- Validate component and parameter existence before operations
- Handle both `IGH_Component` and `IGH_Param` objects for wire connections
- Return success/failure status with detailed error messages



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


### [ ] Enable Dynamic Data Preview Control

**Current State:** Data preview functionality exists but is disabled by default.

**Goal:** Allow Claude to toggle data previews without rebuilding the plugin.

**What's needed:**
- **New MCP Tool:** `set_data_preview_mode`
- **New Endpoint:** `set_preview_mode` in Grasshopper plugin
- **Location:** Currently at `grasshopper-plugin/LiveCodingGH/LiveCodingComponent.cs:279`


### [ ]  Canvas State Diffing
**Goal:** Compare canvas states before/after operations to track changes.

**Tool Name:** `mcp__grasshopper-bridge__canvas_diff`

