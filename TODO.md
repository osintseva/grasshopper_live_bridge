# 🎯 Grasshopper Live Bridge - Development TODO

This document tracks the development roadmap for enhanced Claude Code integration with Grasshopper through the MCP bridge and plugin capabilities.

## 🚨 **TODO #1: HIGHEST PRIORITY**

### [ ] Understand Custom Input/Output Specification Format
**Status:** 📖 **RESEARCH NEEDED - CRITICAL PRIORITY**

**Goal:** Learn how to specify custom inputs/outputs to Python script components when it's created programmatically.

**Current Status:** Implementation exists in code but functionality not verified and needs to be understood.

---

## 🚨 **TODO #2: SECOND PRIORITY**

### [ ] Learn Component Connection Mechanisms
**Status:** 📖 **RESEARCH NEEDED - HIGH PRIORITY**

**Goal:** Understand how to connect Python components to existing components in Grasshopper.

**Current Status:** Connection code exists but needs verification and documentation.

---

## 🧪 **TODO #3: TESTING**

### [ ] Test Python Component Creation Methods in Rhino 8
**Status:** ⚠️ **DEPENDS ON TODO #1 & #2**

**Goal:** Verify which of the three Python component creation methods actually works in current Rhino 8 environment.

**Three Methods to Test:**
1. **`create_python_script`** - Basic Python component creation
2. **`create_python_advanced`** - Uses Rhino 8.14+ official API with `Python3Component.Create`
3. **`create_python_xml`** - XML serialization workaround for older versions

**Testing Checklist:**
- [ ] Test each method in current Rhino 8 environment
- [ ] Verify custom input/output parameter creation works
- [ ] Confirm automatic connections function properly
- [ ] Document which method(s) are reliable for the target environment

**Expected Outcome:** Clear documentation of which endpoint(s) Claude Code should use for Python component creation.

---

## 🐍 Python Component Management

### [ ] **TODO #4:** Create MCP Tool for Python Script Creation
**Status:** 🔧 **DEPENDS ON TODO #1, #2, #3**

**Goal:** Enable Claude Code to execute "convert my script to python code" commands.

**What it does:**
- Creates Python components on Grasshopper canvas programmatically
- Supports custom inputs/outputs and automatic connections
- Integrates with verified working endpoint from TODO #1

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