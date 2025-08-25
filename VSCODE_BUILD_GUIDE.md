# VS Code Build Guide 🛠️

Complete guide for building and developing the Grasshopper Live Bridge with VS Code.

## Prerequisites 📋

1. **Rhino 7** - Required for Grasshopper and RhinoCommon references
2. **VS Code** - https://code.visualstudio.com
3. **.NET SDK** - https://dotnet.microsoft.com/download (.NET Framework 4.8 Developer Pack recommended)
4. **Node.js** - https://nodejs.org (for testing WebSocket API)

### VS Code Extensions
```bash
code --install-extension ms-dotnettools.csharp
```

## Quick Build ⚡

```bash
cd grasshopper_component/LiveCodingGH
code .                           # Open in VS Code
dotnet build -c Release         # Build for production
```

**Output:** `bin/Release/net48/LiveCodingGH.gha`

## Installation 📦

```bash
# Install the plugin
copy "bin\Release\net48\LiveCodingGH.gha" "%APPDATA%\Grasshopper\Libraries\"

# Test the installation
cd ../../
npm install
node scripts/test_connection.js get_canvas_info --wait=5000
```

## Project Structure 📁

```
grasshopper_component/LiveCodingGH/
├── LiveCodingComponent.cs      # Main WebSocket server component
├── LiveCodingGHInfo.cs         # Plugin metadata
├── Properties/AssemblyInfo.cs  # Assembly information
├── LiveCodingGH.csproj         # Project configuration
└── bin/                        # Build outputs (.gha files)
```

## Development Workflow 🔄

### 1. Make Changes
Edit C# files in VS Code with full IntelliSense support.

### 2. Build & Test
```bash
dotnet build                    # Debug build
node ../../scripts/test_connection.js ping
```

### 3. Install for Testing

**Quick Test (Drag & Drop):**
1. Open Rhino → Grasshopper
2. Drag `bin/Debug/net48/LiveCodingGH.gha` onto canvas
3. Confirm loading

**Permanent Install:**
```bash
copy "bin\Debug\net48\LiveCodingGH.gha" "%APPDATA%\Grasshopper\Libraries\"
# Restart Rhino/Grasshopper
```

## Testing the Canvas Feature 🧪

### Setup Test Canvas
1. Open Rhino → Grasshopper  
2. Add "Live Coding Controller (Python)" component (Params → Util)
3. Create a simple definition with sliders, components, connections

### Test WebSocket API
```bash
node scripts/test_connection.js ping                        # Test connection
node scripts/test_connection.js get_canvas_info --wait=5000 # Get canvas data
node scripts/test_connection.js create_slider               # Create components
```

### Expected Output
```json
{
  "action": "get_canvas_info_response",
  "correlationId": "...",
  "status": "success", 
  "data": "{\\"Components\\":[...]}"
}
```

## Troubleshooting 🔧

### Build Issues

**❌ "net48 is not a valid framework"**
```bash
# Install .NET Framework 4.8 Developer Pack
# Download from: https://dotnet.microsoft.com/download/dotnet-framework/net48
```

**❌ Can't find Rhino DLLs**
```powershell
# Find your Rhino installation
Get-ChildItem -Path "C:" -Filter "RhinoCommon.dll" -Recurse -ErrorAction SilentlyContinue
# Update paths in LiveCodingGH.csproj accordingly
```

### Runtime Issues

**❌ Component doesn't appear**
1. Build succeeded? Check for errors in VS Code terminal
2. File unblocked? Right-click .gha → Properties → Unblock
3. Restart Rhino completely

**❌ WebSocket connection fails**
1. Port 8181 available? `netstat -an | findstr 8181`
2. Component on canvas and not disabled?
3. Windows Firewall blocking? Allow Rhino through firewall

**❌ Canvas info returns empty**
1. Components on canvas? Need at least one component
2. Solution computed? Right-click canvas → Recompute

## Advanced Development 🚀

### VS Code Tasks
Create `.vscode/tasks.json`:
```json
{
    "version": "2.0.0",
    "tasks": [
        {
            "label": "build",
            "command": "dotnet",
            "args": ["build"],
            "group": "build",
            "problemMatcher": "$msCompile"
        },
        {
            "label": "build-release", 
            "command": "dotnet",
            "args": ["build", "-c", "Release"],
            "group": "build"
        }
    ]
}
```

Use **Ctrl+Shift+P** → "Tasks: Run Task" → "build"

### Hot Reload Setup
1. Run `GrasshopperDeveloperSettings` in Rhino
2. Enable memory loading
3. Component reloads on each build (no restart needed)

## Integration Examples 🔗

### MCP Server Integration
```javascript
const WebSocket = require('ws');

async function getGrasshopperCanvas() {
  const ws = new WebSocket('ws://localhost:8181/live');
  
  return new Promise((resolve) => {
    ws.on('open', () => {
      ws.send(JSON.stringify({
        action: 'get_canvas_info',
        correlationId: Date.now().toString(),
        payload: {}
      }));
    });
    
    ws.on('message', (data) => {
      const response = JSON.parse(data);
      if (response.action === 'get_canvas_info_response') {
        const canvas = JSON.parse(response.data);
        ws.close();
        resolve(canvas);
      }
    });
  });
}

// Usage
const canvas = await getGrasshopperCanvas();
console.log(`Canvas has ${canvas.Components.length} components`);
```

## Why VS Code for Grasshopper Development?

✅ **Lightweight** - Fast startup, low memory usage  
✅ **Modern Tooling** - Latest .NET SDK, excellent Git integration  
✅ **Free** - No licensing costs  
✅ **Cross-Platform** - Works on Windows, Mac, Linux  
✅ **WebSocket Testing** - Great for API development