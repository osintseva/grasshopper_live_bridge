# Remote Python Components Creation

## Overview

The Grasshopper Live Bridge enables **remote creation and management of Python components** in Grasshopper 3D through WebSocket communication. External applications can dynamically create, update, and control GHPython components by sending commands over a network connection.

## How Remote Python Creation Works

### WebSocket Communication Flow

```
External Script                    Grasshopper Component
     │                                      │
     │ 1. Connect to ws://localhost:8181    │
     │─────────────────────────────────────►│
     │                                      │
     │ 2. Send create_python_script command │
     │─────────────────────────────────────►│ 3. Queue command
     │                                      │
     │                                      │ 4. Process command (50ms timer)
     │                                      │
     │                                      │ 5. Create Python component
     │ 6. Receive acknowledgment            │    on Grasshopper canvas
     │◄─────────────────────────────────────│
```

### Python File Integration Process

1. **File Reading** - External script reads Python file content:
   ```javascript
   // scripts/test_connection.js:60
   const code = filePath ? fs.readFileSync(path.resolve(filePath), 'utf8') : "print('default')";
   ```

2. **WebSocket Transmission** - File content sent as JSON payload:
   ```json
   {
     "action": "create_python_script",
     "payload": {
       "x": 260, "y": 160,
       "code": "print('i finally managed')"  // ← File content as string
     }
   }
   ```

3. **Component Creation** - Grasshopper injects code into new Python component:
   - **Rhino 8**: Uses `RhinoCodePluginGH.Components.Python3Component.Create()`
   - **Legacy**: Uses `GhPython.Component.ZuiPythonComponent` with reflection

## Dual Python System Support

### Primary: Rhino 8 RhinoCode (CPython 3.x)
```csharp
// LiveCodingComponent.cs:197-261
private bool TryCreateRhino8Python(GH_Document doc, float x, float y, string code)
{
    var t = Type.GetType("RhinoCodePluginGH.Components.Python3Component, RhinoCodePluginGH");
    if (t == null) return false;
    
    // Try Create(string nickname, string source) overload
    var createStringString = methods.FirstOrDefault(m => /* string, string params */);
    var obj = createStringString.Invoke(null, new object[] { "LivePython", code });
}
```

### Fallback: Legacy GHPython (IronPython 2.x)
```csharp
// LiveCodingComponent.cs:263-281
private bool TryCreateLegacyGhPython(GH_Document doc, float x, float y, string code)
{
    var t = Type.GetType("GhPython.Component.ZuiPythonComponent, GhPython");
    var obj = Activator.CreateInstance(t) as IGH_DocumentObject;
    
    // Inject code using reflection
    TrySetScriptCode(obj, code, out _);
}
```

## Remote Commands API

### Create Python Component
```bash
# Create from file
node scripts/test_connection.js create_python_script scripts/testing/test_script.py

# Create with default code
node scripts/test_connection.js create_python_script
```

**Command Structure**:
```json
{
  "action": "create_python_script",
  "correlationId": "timestamp",
  "payload": {
    "x": 260,        // Canvas X position (optional)
    "y": 160,        // Canvas Y position (optional)  
    "code": "..."    // Python code string (optional)
  }
}
```

### Update Existing Component
```bash
node scripts/test_connection.js update_script <COMPONENT-GUID> path/to/new/code.py
```

**Command Structure**:
```json
{
  "action": "update_script", 
  "payload": {
    "componentId": "12345678-1234-1234-1234-123456789abc",
    "code": "print('updated code')"
  }
}
```

## Code Injection Mechanisms

### Rhino 8 Method (`TrySetRhino8Source`)
- Uses `SetSource()` method directly on RhinoCode components
- Clean, supported API approach

### Legacy Reflection Method (`TrySetScriptCode`)
Searches for writable code properties in this order:
1. **Properties**: `ScriptSource`, `SourceCode`, `Code`, `Text`, `Source`
2. **Fields**: `m_py_code`, `m_script`, `_script`, `_code`, `m_source`  
3. **Nested Objects**: Searches within `Script` and `Editor` properties/fields

```csharp
// LiveCodingComponent.cs:346-409
private static bool TrySetScriptCode(object target, string code, out string debug)
{
    // Try direct properties first
    var propNames = new[] { "ScriptSource", "SourceCode", "Code", "Text", "Source" };
    foreach (var name in propNames) {
        var p = target.GetType().GetProperty(name, BindingFlags.All);
        if (p?.CanWrite == true && p.PropertyType == typeof(string)) {
            p.SetValue(target, code);
            return true;
        }
    }
    
    // Try nested objects (Script.Code, Editor.Source, etc.)
    foreach (var owner in new[] { "Script", "Editor" }) {
        // Search nested properties...
    }
}
```

## Error Handling & Fallbacks

### Creation Hierarchy
1. **Try Rhino 8 RhinoCode** → Modern CPython approach
2. **Try Legacy GHPython** → IronPython fallback  
3. **Create Info Panel** → User feedback if both fail

### Fallback Panel (`FallbackPanel`)
When Python plugins are unavailable:
```csharp
private static void FallbackPanel(GH_Document doc, float x, float y, string message)
{
    var panel = new GH_Panel();
    panel.UserText = "Could not create a Python script component.\n" +
                    "Rhino 8: ensure RhinoCode plugin is loaded.\n" +
                    "Legacy: ensure GHPython is installed.";
    doc.AddObject(panel, true);
}
```

## Usage Examples

### Basic Remote Python Creation
```bash
# Create component with simple code
echo 'print("Hello from remote!")' > temp.py
node scripts/test_connection.js create_python_script temp.py
```

### File-Based Development Workflow
```bash
# 1. Write Python code in external file
echo 'import math; A = math.pi' > scripts/testing/calculation.py

# 2. Push to Grasshopper canvas  
node scripts/test_connection.js create_python_script scripts/testing/calculation.py

# 3. Update existing component
node scripts/test_connection.js update_script <GUID> scripts/testing/updated_calculation.py
```

### Integration with External Editors
The system enables using any external Python editor:
1. Write code in VSCode, PyCharm, Sublime, etc.
2. Save file in `scripts/testing/` folder
3. Push to Grasshopper via WebSocket command
4. Component appears on canvas with your code

## Client Implementation Notes

### File Reading (`test_connection.js`)
- Uses `fs.readFileSync()` for synchronous file reading
- Resolves file paths with `path.resolve()` 
- Transmits entire file content as string payload
- No file references - content is **embedded** in components

### Connection Management
- Auto-connects to `ws://localhost:8181/live`
- Configurable timeout: `--wait=5000` (5 seconds)
- Environment override: `GH_WS_URL=ws://custom:port/path`

## Performance & Limitations

### Advantages
- **Real-time**: Components appear instantly on canvas
- **Cross-Version**: Works with both Rhino 7/8 Python systems
- **File-Based**: Use any external editor for Python development
- **Batch Operations**: Can create multiple components programmatically

### Limitations  
- **Local Only**: WebSocket server binds to localhost
- **No File Watching**: Manual command execution required
- **Code Embedding**: Files are copied, not referenced
- **Reflection Overhead**: Legacy mode uses reflection for code injection

## Security Considerations

- **Trusted Sources Only**: Direct Python code execution
- **Local Network**: No external network exposure
- **File Access**: Client can read any accessible file on system
- **No Authentication**: Designed for development use only