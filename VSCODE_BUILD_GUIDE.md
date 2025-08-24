# Building LiveCodingGH Plugin with VS Code

### Modern Development Workflow for Grasshopper Components

This guide shows how to build and develop the LiveCodingGH plugin using VS Code and .NET CLI - no Visual Studio required! Perfect for lightweight development or cross-platform work.

## Prerequisites

### Required Software
1. **Rhino 8** (recommended) or Rhino 7
2. **VS Code** - https://code.visualstudio.com
3. **.NET SDK** - https://dotnet.microsoft.com/download
   - .NET Framework 4.8 Developer Pack (recommended)
   - Or newer .NET 6+ with Windows target
4. **Node.js** (for testing WebSocket) - https://nodejs.org

### VS Code Extensions (Recommended)
```bash
# Install these extensions for better C# development
code --install-extension ms-dotnettools.csharp
code --install-extension ms-dotnettools.vscode-dotnet-runtime
```

## Quick Start (Existing Project)

Since the project is already set up, you can jump right into building:

### 1. Open Project in VS Code
```bash
# Navigate to the project
cd grasshopper_component/LiveCodingGH

# Open in VS Code
code .
```

### 2. Build the Plugin
In VS Code terminal (Terminal → New Terminal):

```bash
# Debug build (for development)
dotnet build

# Release build (for distribution)
dotnet build -c Release
```

### 3. Locate Your .gha File
After successful build, find:
- **Debug**: `bin/Debug/net48/LiveCodingGH.gha`
- **Release**: `bin/Release/net48/LiveCodingGH.gha`

### 4. Install & Test
```bash
# Copy to Grasshopper Libraries folder
copy "bin\Release\net48\LiveCodingGH.gha" "%APPDATA%\Grasshopper\Libraries\"

# Test WebSocket connection
cd ../../
npm install
node scripts/test_connection.js get_canvas_info --wait=5000
```

## Project Structure

The LiveCodingGH project is already configured with:

```
grasshopper_component/LiveCodingGH/
├── LiveCodingComponent.cs      # Main WebSocket server component
├── LiveCodingGHInfo.cs         # Plugin metadata
├── Properties/
│   └── AssemblyInfo.cs         # Assembly information
├── LiveCodingGH.csproj         # Project configuration
└── bin/                        # Build outputs (.gha files)
```

## Understanding the Project Configuration

### LiveCodingGH.csproj Breakdown

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>    <!-- .NET Framework 4.8 -->
    <AssemblyName>LiveCodingGH</AssemblyName>   <!-- Output name -->
    <LangVersion>latest</LangVersion>           <!-- Latest C# features -->
  </PropertyGroup>

  <ItemGroup>
    <!-- NuGet packages for WebSocket and JSON -->
    <PackageReference Include="WebSocketSharp-netstandard" Version="1.0.1" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>

  <ItemGroup>
    <!-- Rhino 8 references (update paths if needed) -->
    <Reference Include="RhinoCommon">
      <HintPath>C:\Program Files\Rhino 8\System\RhinoCommon.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="Grasshopper">
      <HintPath>C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Grasshopper.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="GH_IO">
      <HintPath>C:\Program Files\Rhino 8\Plug-ins\Grasshopper\GH_IO.dll</HintPath>
      <Private>False</Private>
    </Reference>
  </ItemGroup>

  <!-- Auto-create .gha file after build -->
  <Target Name="PostBuild" AfterTargets="PostBuildEvent">
    <Copy SourceFiles="$(TargetPath)" DestinationFiles="$(TargetDir)$(AssemblyName).gha" />
    <Message Text="✅ Created $(AssemblyName).gha in $(TargetDir)" Importance="high" />
  </Target>

</Project>
```

## Development Workflow

### 1. Make Code Changes
Edit `LiveCodingComponent.cs` or other files in VS Code with full IntelliSense support.

### 2. Build & Test Cycle
```bash
# Quick build
dotnet build

# Test immediately
node ../../scripts/test_connection.js ping
```

### 3. Install for Testing
**Option A: Drag & Drop (Quick testing)**
1. Open Rhino → Grasshopper  
2. Drag `bin/Debug/net48/LiveCodingGH.gha` onto canvas
3. Confirm loading

**Option B: Permanent Install**
```bash
# Copy to permanent location
copy "bin\Debug\net48\LiveCodingGH.gha" "%APPDATA%\Grasshopper\Libraries\"

# Then restart Rhino/Grasshopper
```

### 4. Debug Output
Add debug statements to your code:
```csharp
// This appears in Rhino command line
Rhino.RhinoApp.WriteLine($"Debug: WebSocket server started on port {WS_PORT}");

// This appears in Grasshopper component messages
AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Canvas info sent via WebSocket");
```

## Testing the Canvas Info Feature

### Setup Test Canvas
1. Open Rhino → Grasshopper
2. Add the "Live Coding Controller (Python)" component from **Params → Util**
3. Create a simple definition:
   - Add a Number Slider
   - Add a Point component
   - Connect them

### Test WebSocket API
```bash
# Test connection
node scripts/test_connection.js ping

# Get canvas information 
node scripts/test_connection.js get_canvas_info --wait=5000

# Create components remotely
node scripts/test_connection.js create_slider
```

### Expected Output (get_canvas_info)
```json
{
  "action": "get_canvas_info_response",
  "correlationId": "...",
  "status": "success", 
  "data": "{\"Components\":[{\"Id\":0,\"Name\":\"Number Slider\",\"NickName\":\"N\",\"Description\":\"Numeric slider for single values\",\"IsComponent\":false,\"Inputs\":[],\"Outputs\":[{\"Name\":\"N\",\"TypeName\":\"Number (floating point)\",\"DataPreview\":\"5.00\"}]},{\"Id\":1,\"Name\":\"Construct Point\",\"NickName\":\"Pt\",\"Description\":\"Construct a point from {xyz} coordinates\",\"IsComponent\":true,\"Inputs\":[{\"Name\":\"X\",\"TypeName\":\"Number (floating point)\",\"Connections\":[[0,0]]}],\"Outputs\":[{\"Name\":\"Point\",\"TypeName\":\"Point (point3d)\",\"DataPreview\":\"Pt(5.00,0.00,0.00)\"}]}]}"
}
```

## Troubleshooting

### Build Issues

**❌ "net48 is not a valid framework"**
```bash
# Option 1: Install .NET Framework 4.8 Developer Pack
# Download from: https://dotnet.microsoft.com/download/dotnet-framework/net48

# Option 2: Change target framework in .csproj
<TargetFramework>net6.0-windows</TargetFramework>
```

**❌ Can't find Rhino DLLs**
```powershell
# Find your Rhino installation
Get-ChildItem -Path "C:\" -Filter "RhinoCommon.dll" -Recurse -ErrorAction SilentlyContinue | Select-Object DirectoryName

# Update paths in LiveCodingGH.csproj accordingly
```

**❌ Missing compiler required member**
```bash
dotnet add package Microsoft.CSharp --version 4.7.0
```

### Runtime Issues

**❌ Component doesn't appear**
1. Build succeeded? Check for errors
2. File unblocked? Right-click .gha → Properties → Unblock
3. Restart Rhino completely
4. Check Grasshopper Developer Settings

**❌ WebSocket connection fails**
1. Port 8181 available? Check with `netstat -an | findstr 8181`
2. Windows Firewall? Allow Rhino through firewall
3. Component running? Must be on canvas and not disabled

**❌ Canvas info returns empty**
1. Components on canvas? Must have at least one component
2. Solution computed? Right-click canvas → Recompute
3. Component timeout? Increase wait time in test script

### Performance Issues

**⚠️ Large canvases (100+ components)**
- May take 5-10 seconds to analyze
- Data previews are automatically truncated
- Consider filtering specific component types if needed

**⚠️ Memory usage**
- WebSocket server runs continuously
- Close Rhino to fully stop server
- Monitor memory if running long sessions

## Advanced Development

### Hot Reload Setup
For faster development cycles:

1. **GrasshopperDeveloperSettings** in Rhino command line
2. Enable memory loading
3. Component reloads on each build (no restart needed)

### Custom Build Script
Create `build.cmd` for quick builds:
```batch
@echo off
cls
echo Building LiveCodingGH...
dotnet build -c Release
if %errorlevel% == 0 (
    echo.
    echo ✅ Build successful!
    echo 📁 Find .gha in: bin\Release\net48\
    echo.
    echo Quick install:
    echo copy "bin\Release\net48\LiveCodingGH.gha" "%%APPDATA%%\Grasshopper\Libraries\"
) else (
    echo ❌ Build failed!
)
pause
```

### VS Code Tasks
Add to `.vscode/tasks.json`:
```json
{
    "version": "2.0.0",
    "tasks": [
        {
            "label": "build",
            "command": "dotnet",
            "type": "process",
            "args": ["build"],
            "group": "build",
            "problemMatcher": "$msCompile"
        },
        {
            "label": "build-release", 
            "command": "dotnet",
            "type": "process",
            "args": ["build", "-c", "Release"],
            "group": "build"
        }
    ]
}
```

Now use **Ctrl+Shift+P** → "Tasks: Run Task" → "build"

## Integration Examples

### MCP Server Integration
The canvas info endpoint is perfect for MCP servers:

```javascript
// Simple MCP integration
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

✅ **Lightweight & Fast** - Starts instantly, uses less RAM than Visual Studio

✅ **Cross-Platform** - Works on Windows, Mac, Linux (Rhino 8 Mac support)

✅ **Modern Tooling** - Latest .NET SDK, NuGet integration, Git support

✅ **Free & Open Source** - No licensing costs or restrictions

✅ **Extensible** - Rich ecosystem of extensions

✅ **WebSocket Development** - Great for testing REST/WebSocket APIs

✅ **Version Control** - Excellent Git integration for team development

---

**🎉 Happy coding! Your LiveCodingGH plugin is now ready for development with VS Code.**

**📖 Next Steps:**
- See [USAGE_GUIDE.md](USAGE_GUIDE.md) for API documentation
- Check [BUILD_INSTRUCTIONS.md](BUILD_INSTRUCTIONS.md) for Visual Studio builds
- Explore the WebSocket API with `scripts/test_connection.js`