# 🐍 Rhino 8 Python Component Creation Testing Guide

This guide will help you test the new programmatic Python component creation features for Grasshopper in Rhino 8.

## 🎯 Overview

Three methods are now available to create Python components programmatically:

1. **`create_python_script`** - Original basic method (existing functionality)
2. **`create_python_advanced`** - Uses the official Rhino 8.14+ `Python3Component.Create` API ⭐ NEW
3. **`create_python_xml`** - Uses XML serialization workaround for older versions ⭐ NEW

The new endpoints support:
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

🐙 Testing Basic Python Script Creation (Original Method)...
📤 Sending: create_python_script
✅ Basic Python script component created successfully!

🚀 Testing Advanced Python Component Creation (Rhino 8.14+ API)...
📤 Sending: create_python_advanced
✅ Advanced Python component created successfully!

📋 Testing XML-based Python Component Creation...
📤 Sending: create_python_xml
✅ XML-based Python component created successfully!

📊 Getting canvas information...
📤 Sending: get_canvas_info
✅ Canvas info retrieved successfully!
📈 # Components: 6 | Connections: 3 | Sources: 2 | Sinks: 1

📋 Test Results Summary:
==============================
ping                 ✅ PASS
create_slider        ✅ PASS
basic_python         ✅ PASS
advanced_python      ✅ PASS
xml_python           ✅ PASS
canvas_info          ✅ PASS

🎯 Total: 6/6 tests passed
🎉 All tests passed! All three Python creation methods are working.

🔌 Disconnected from Grasshopper

🚀 Ready to use programmatic Python component creation!
```

### Method 2: Manual Testing

You can also test each endpoint individually using any WebSocket client or create your own script.

#### Test Basic Python Script (Original Method)

```json
{
  "action": "create_python_script",
  "correlationId": "test-000",
  "payload": {
    "x": 500,
    "y": 150,
    "code": "# Basic Python Test\nimport Rhino.Geometry as rg\nA = 'Hello from basic Python!'\nB = rg.Point3d(1, 2, 3)"
  }
}
```

#### Test Advanced Python Component

```json
{
  "action": "create_python_advanced",
  "correlationId": "test-001",
  "payload": {
    "x": 200,
    "y": 150,
    "code": "# Test Component\nimport Rhino.Geometry as rg\nresult = f'Hello from Python at {System.DateTime.Now}'",
    "inputs": [
      {
        "name": "input_data",
        "nickname": "data",
        "optional": true,
        "access": "list"
      }
    ],
    "outputs": [
      {
        "name": "result",
        "nickname": "out"
      }
    ],
    "connections": []
  }
}
```

#### Test XML Python Component

```json
{
  "action": "create_python_xml",
  "correlationId": "test-002",
  "payload": {
    "x": 400,
    "y": 150,
    "code": "# XML Test\nimport math\noutput = math.pi * 2",
    "inputs": [
      {
        "name": "multiplier",
        "nickname": "mult"
      }
    ],
    "outputs": [
      {
        "name": "output",
        "nickname": "result"
      }
    ],
    "connections": []
  }
}
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

## 🔍 Troubleshooting

### Common Issues

#### ❌ Connection Failed
```
❌ Connection failed: [Errno 10061] No connection could be made
```
**Solution:**
- Ensure Grasshopper is running
- Verify LiveCodingComponent is on canvas
- Check that WebSocket server is active

#### ❌ API Not Available
```
❌ Advanced Python creation failed: Python3Component type not found
```
**Solution:**
- Update to Rhino 8.14+ for best compatibility
- Use `create_python_xml` endpoint as fallback
- Ensure RhinoCode plugin is installed

#### ❌ Parameter Creation Failed
```
❌ XML Python creation failed: Could not access component parameter server
```
**Solution:**
- Check Python component plugin installation
- Try simpler parameter specifications
- Verify Grasshopper component framework compatibility

#### ❌ Connection Timeout
```
⏰ Timeout waiting for response to create_python_advanced
```
**Solution:**
- Increase timeout in test script
- Check Grasshopper is responsive
- Verify component creation isn't blocked by dialogs

### Debug Steps

1. **Check Grasshopper Component Output:**
   - Look at "Debug Log" output for detailed error messages
   - Check "Last Command" shows your action was received

2. **Verify Canvas State:**
   - Run `get_canvas_info` to see current components
   - Check if components were created but positioning failed

3. **Test Basic Functionality:**
   - Try `ping` command first
   - Create simple slider with `create_slider`
   - Verify basic WebSocket communication

## 🎉 Success Indicators

### Full Success (Rhino 8.14+)
- ✅ Both `create_python_advanced` and `create_python_xml` work
- ✅ Components appear on canvas with custom parameters
- ✅ Connections are established automatically
- ✅ Python code executes correctly

### Partial Success (Older Rhino 8)
- ❌ `create_python_advanced` fails with API not found
- ✅ `create_python_xml` works
- ✅ Components created with basic functionality

### Minimal Success
- ❌ Both new endpoints fail
- ✅ Existing `create_python_script` still works
- ⚠️ Limited to basic Python component creation

## 🚀 Next Steps

Once testing is successful, you can:

1. **Integrate with MCP Server:** Add these endpoints to your hybrid server
2. **Build UI Tools:** Create interfaces for visual Python component creation
3. **Automate Workflows:** Use programmatic creation in larger automation scripts
4. **Extend Functionality:** Add support for C# script components using similar patterns

## 📖 API Reference

### WebSocket Message Format

**Request:**
```json
{
  "action": "create_python_advanced|create_python_xml",
  "correlationId": "unique-id",
  "payload": { /* endpoint-specific payload */ }
}
```

**Response (Queued):**
```json
{
  "action": "create_python_advanced_response",
  "correlationId": "unique-id",
  "status": "queued"
}
```

**Response (Success):**
```json
{
  "action": "create_python_advanced_response",
  "correlationId": "unique-id",
  "status": "success"
}
```

**Response (Error):**
```json
{
  "action": "create_python_advanced_response",
  "correlationId": "unique-id",
  "status": "error",
  "message": "Error description"
}
```

---

## 🎯 Summary

You now have three robust methods for creating Python components programmatically in Rhino 8:

- **Basic Method:** `create_python_script` - Original functionality, simple and reliable
- **Modern Approach:** `create_python_advanced` - Full-featured for Rhino 8.14+ with custom parameters
- **Fallback Approach:** `create_python_xml` - Advanced features for older versions using XML workaround

The comprehensive testing suite verifies all three methods, ensuring you can choose the best approach for your specific Rhino version and requirements - enabling powerful automation workflows for Grasshopper development! 🚀