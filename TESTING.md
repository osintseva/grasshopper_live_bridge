# 🐍 Python Component Creation Testing Guide

This guide will help you test the Python component creation feature for Grasshopper in Rhino 8.

## 🎯 Overview

The **`create_python_component`** endpoint enables programmatic Python component creation using the proven RhinoCodePluginGH API with ScriptVariableParam support.

Features:
- ✅ Custom input/output parameter specification using ScriptVariableParam
- ✅ Custom Python code injection
- ✅ Automatic component connections
- ✅ Position control on canvas
- ✅ Robust method overload handling
- ✅ Type hints and parameter configuration

## 🔧 Prerequisites

### Required Software
- **Rhino 8** with RhinoCode plugin loaded
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

### Automated Test Script

Navigate to your project directory and run the comprehensive test:

```bash
cd scripts/testing
python test_script.py
```

**Expected Output:**
```
🐍 Python Component Creation & Connection Tester
============================================================
🔧 Testing: Proven Method + Component Creation + Custom I/O + Connections
============================================================
🧪 Starting Python Component Creation Test
============================================================
🔌 Connecting to Grasshopper...
✅ Connected successfully!

🏓 Testing ping...
📤 Sending: ping
✅ Ping successful!

🔧 Creating source components for connections...
📤 Sending: create_slider
📤 Sending: create_slider
📤 Sending: create_python_component
✅ Created 3/3 source components successfully!

🎯 Testing Python Component Creation (Proven Method)...
📤 Sending: create_python_component
✅ Python component created successfully!

📊 Getting canvas information...
📤 Sending: get_canvas_info
✅ Canvas info retrieved successfully!
📈 # Components: 5 | Connections: 3 | Sources: 3 | Sinks: 1

🔗 Verifying component connections...
📤 Sending: get_canvas_info
📊 Found 4 components with potential inputs
🔗 Found 3 components with connections
✅ Connection verification successful!

📋 Test Results Summary:
==============================
ping                 ✅ PASS
create_sources       ✅ PASS
python_component     ✅ PASS
canvas_info          ✅ PASS
verify_connections   ✅ PASS

🎯 Total: 5/5 tests passed
🎉 All tests passed! Python component creation system working perfectly!

🔧 Method Status:
✅ create_python_component - Working (ScriptVariableParam API)

🔌 Disconnected from Grasshopper

🚀 Ready to use Python component creation!
```

## 🎯 What Gets Tested

The test script validates:

1. **Basic Connectivity** - WebSocket connection to Grasshopper
2. **Source Component Creation** - Sliders and Python components for testing
3. **Python Component Creation** - Main functionality using ScriptVariableParam API
4. **Canvas Analysis** - Retrieving and parsing canvas information
5. **Connection Verification** - Ensuring automatic connections work properly

Each test provides detailed feedback about what succeeded or failed, making it easy to identify and resolve issues.

---

*Ready to create Python components programmatically! 🎉*