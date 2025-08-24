# Build and Installation Instructions

## Prerequisites

1. **Rhino 8** installed (required for Grasshopper and RhinoCommon references)
2. **Development Environment** - Choose one:
   - **VS Code** + .NET SDK (recommended - see [VSCODE_BUILD_GUIDE.md](VSCODE_BUILD_GUIDE.md))
   - **Visual Studio 2019/2022** with .NET Framework support
3. **Node.js** (for testing WebSocket API)

## Building the Plugin

### Option 1: Using VS Code (Recommended)

**📖 See [VSCODE_BUILD_GUIDE.md](VSCODE_BUILD_GUIDE.md) for detailed VS Code development workflow**

Quick build:
```bash
cd grasshopper_component/LiveCodingGH
dotnet build -c Release
```

### Option 2: Using Visual Studio

1. Open `grasshopper_component/LiveCodingGH/LiveCodingGH.csproj` in Visual Studio
2. Set configuration to **Release** for distribution
3. Build solution (`Build > Build Solution` or `Ctrl+Shift+B`)
4. Find `.gha` file in `bin/Release/net48/` folder

### Option 3: Command Line (MSBuild)

```bash
cd grasshopper_component/LiveCodingGH
dotnet build -c Release

# Alternative with MSBuild directly
MSBuild.exe LiveCodingGH.csproj /p:Configuration=Release
```

## Installation

1. **Locate the compiled `.gha` file**:
   - After building, you'll find `LiveCodingGH.gha` in:
     - `grasshopper_component/LiveCodingGH/bin/Release/LiveCodingGH.gha` (Release build)
     - `grasshopper_component/LiveCodingGH/bin/Debug/LiveCodingGH.gha` (Debug build)

2. **Install the plugin**:
   - Copy `LiveCodingGH.gha` to your Grasshopper components folder:
     - **Windows**: `%APPDATA%\Grasshopper\Libraries\`
     - **Example**: `C:\Users\[username]\AppData\Roaming\Grasshopper\Libraries\`

3. **Unblock the file** (Windows security):
   - Right-click on `LiveCodingGH.gha`
   - Select "Properties"
   - If there's an "Unblock" button at the bottom, click it
   - Click "OK"

4. **Restart Rhino** if it was running

## Verification

1. Open Rhino 8
2. Type `Grasshopper` to launch Grasshopper
3. Look for the **Live Coding Controller (Python)** component under:
   - **Tab**: Params
   - **Panel**: Util

## Troubleshooting

### Common Issues

1. **"Could not load file or assembly" error**:
   - Make sure Rhino 8 is installed
   - Verify the .gha file is unblocked (see installation step 3)
   - Check that all dependencies are available

2. **Component doesn't appear**:
   - Make sure the .gha file is in the correct Grasshopper Libraries folder
   - Restart Rhino completely
   - Check if the file is blocked by Windows security

3. **WebSocket connection fails**:
   - Make sure no other application is using port 8181
   - Check Windows Firewall settings
   - Try running as administrator

### Dependencies

The plugin automatically includes these NuGet packages:
- `WebSocketSharp-netstandard` (1.0.1) - WebSocket server functionality
- `Newtonsoft.Json` (13.0.3) - JSON serialization

System references:
- `RhinoCommon.dll` - Rhino geometry and core functionality
- `Grasshopper.dll` - Grasshopper component framework
- `GH_IO.dll` - Grasshopper data handling
- `System.Windows.Forms` - UI components
- `System.Drawing` - Graphics and drawing

## Development

### Debug Build

For development, use Debug configuration:

```bash
dotnet build -c Debug
```

This enables debugging symbols and detailed error messages.

### Modifying Rhino Paths

If Rhino is installed in a non-standard location, update the reference paths in `LiveCodingGH.csproj`:

```xml
<Reference Include="RhinoCommon">
  <HintPath>C:\Your\Rhino\Path\System\RhinoCommon.dll</HintPath>
  <Private>False</Private>
</Reference>
```