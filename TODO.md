# 🎯 Grasshopper Live Bridge - Development TODO

This document tracks the development roadmap for enhanced Claude Code integration with Grasshopper through the MCP bridge and plugin capabilities.

## 🚨 **TODO #1: HIGHEST PRIORITY**

### [✅] Understand Custom Input/Output Specification Format
**Status:** ✅ **DONE**

**Goal:** Learn how to specify custom inputs/outputs to Python script components when it's created programmatically.


---

## 🚨 **TODO #2: SECOND PRIORITY**

### [✅] Learn Component Connection Mechanisms
**Status:** ✅ **COMPLETED - CONNECTION SYSTEM WORKING**

**Goal:** Understand how to connect Python components to existing components in Grasshopper.

**Solution:** Fixed type casting issue where sliders (`IGH_Param`) weren't being found by component search (`IGH_Component`). Implemented multi-strategy search supporting both component and parameter objects, with robust UUID and nickname-based connection resolution.

**Key Fixes:**
- Enhanced `MakeConnections()` to handle both `IGH_Component` and `IGH_Param` objects
- Fixed timing issue by making connections immediately after component creation (outside solution context)
- Added 5-strategy search: full GUID, formatted GUID, component nickname, parameter nickname, partial UUID
- Validated working connections between sliders and Python components

---

## 🧪 **TODO #3: TESTING**

### [✅] Test Python Component Creation Method in Rhino 8
**Status:** ✅ **COMPLETED - PROVEN METHOD VALIDATED**

**Goal:** ~~Verify which of the three Python component creation methods actually works~~ **UPDATED:** Single proven method validated and working.

**Proven Method:**
1. **`create_python_component`** - Uses ScriptVariableParam API with RhinoCodePluginGH.Components.Python3Component

**Testing Checklist:**
- [✅] Test proven method in current Rhino 8 environment
- [✅] Verify custom input/output parameter creation works with ScriptVariableParam
- [✅] Document working endpoint for Claude Code integration

**Outcome:** ✅ **`create_python_component` endpoint validated and working reliably for Python component creation with custom I/O and connections.**

---

## 🐍 Python Component Management

### [✅] **TODO #4:** Create MCP Tool for Python Script Creation
**Status:** 🔧 **IMPLEMENTED**

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

---

## 🔍 Component Context Analysis

### [ ] **TODO #5:** Add Component Context Retrieval Tool
**Status:** 💡 **NEW FEATURE**

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

---

## 📊 Data Preview Enhancement

### [ ] **TODO #6:** Enable Dynamic Data Preview Control
**Status:** ⚙️ **NEEDS CONFIGURATION**

**Current State:** Data preview functionality exists but is disabled by default.

**Goal:** Allow Claude to toggle data previews without rebuilding the plugin.

**What's needed:**
- **New MCP Tool:** `set_data_preview_mode`
- **New Endpoint:** `set_preview_mode` in Grasshopper plugin
- **Location:** Currently at `grasshopper-plugin/LiveCodingGH/LiveCodingComponent.cs:279`


---

## 🚀 Future Enhancements

### [ ] **Future:** Advanced Component Querying
**Goal:** Enhanced component search with multiple criteria filtering.
**Tool Name:** `mcp__grasshopper-bridge__advanced_component_search`

### [ ] **Future:** Canvas State Diffing
**Goal:** Compare canvas states before/after operations to track changes.
**Tool Name:** `mcp__grasshopper-bridge__canvas_diff`


---

**Legend:**
✅ Completed | ⚠️ Needs Configuration | 🔧 In Progress | ❌ Blocked