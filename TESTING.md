# 🐍 Rhino 8 Python Component Creation Testing Guide

This guide will help you test the new programmatic Python component creation features for Grasshopper in Rhino 8.

## 🎯 Overview

Two new endpoints have been added to create Python components programmatically:

1. **`create_python_advanced`** - Uses the official Rhino 8.14+ `Python3Component.Create` API
2. **`create_python_xml`** - Uses XML serialization workaround for older versions

Both endpoints support:
- ✅ Custom input/output parameter specification
- ✅ Custom Python code injection
- ✅ Automatic component connections
- ✅ Position control on canvas

## 🔧 Prerequisites

### Required Software
- **Rhino 8** (any version, but 8.14+ recommended for best results)
- **Grasshopper** loaded with your LiveCodingComponent plugin
- **Python 3.7+** with `websockets` package

### Installation Steps

1. **Install Python dependencies:**
   ```bash
   pip install websockets
   ```

2. **Load Grasshopper Plugin:**
   - Open Rhino 8
   - Start Grasshopper (`_Grasshopper`)
   - Add your LiveCodingComponent to the canvas
   - Verify WebSocket server is running on `ws://localhost:8181/live`

3. **Verify Connection:**
   - Component should show "Server: ws://localhost:8181/live (OK)" status

## 🧪 Running the Tests

### Method 1: Automated Test Script

Navigate to your project directory and run the comprehensive test:

```bash
cd scripts/testing
python test_script.py
```

**Expected Output:**
```
🐍 Rhino 8 Python Component Creation Tester
==================================================
🧪 Starting Rhino 8 Python Component Creation Tests
============================================================
🔌 Connecting to Grasshopper...
✅ Connected successfully!

🏓 Testing ping...
📤 Sending: ping
✅ Ping successful!

🎚️ Creating test slider for connections...
📤 Sending: create_slider
✅ Test slider created!

🚀 Testing Advanced Python Component Creation (Rhino 8.14+ API)...
📤 Sending: create_python_advanced
✅ Advanced Python component created successfully!

📋 Testing XML-based Python Component Creation...
📤 Sending: create_python_xml
✅ XML-based Python component created successfully!

📊 Getting canvas information...
📤 Sending: get_canvas_info
✅ Canvas info retrieved successfully!
📈 # Components: 5 | Connections: 2 | Sources: 2 | Sinks: 1

📋 Test Results Summary:
==============================
ping                 ✅ PASS
create_slider        ✅ PASS
advanced_python      ✅ PASS
xml_python           ✅ PASS
canvas_info          ✅ PASS

🎯 Total: 5/5 tests passed
🎉 All tests passed! Both Python creation methods are working.

🔌 Disconnected from Grasshopper

🚀 Ready to use programmatic Python component creation!
```



## 📋 Endpoint Specifications

### `create_python_advanced`

**Purpose:** Creates Python components using the official Rhino 8.14+ API

**Payload Parameters:**
- `x` (float, optional): X position on canvas (default: 300)
- `y` (float, optional): Y position on canvas (default: 200)
- `code` (string, optional): Python code to execute
- `inputs` (array, optional): Input parameter specifications
- `outputs` (array, optional): Output parameter specifications
- `connections` (array, optional): Connection specifications

**Input/Output Specification:**
```json
{
  "name": "parameter_name",
  "nickname": "short_name",
  "optional": true,
  "access": "item|list|tree"
}
```

**Connection Specification:**
```json
{
  "sourceId": "component_guid_or_nickname",
  "sourceOutput": 0,
  "targetInput": 0
}
```

### `create_python_xml`

**Purpose:** Creates Python components using XML serialization workaround

**Parameters:** Same as `create_python_advanced`

**Differences:**
- Works with older Rhino 8 versions
- Uses reflection and XML manipulation
- May have limited parameter type support
- Fallback for when official API is not available
