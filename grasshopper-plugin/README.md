# Grasshopper Plugin - Live Coding Component

C# Grasshopper component that provides WebSocket API for real-time canvas analysis and live coding.

## Prerequisites

- **Rhino 7 or 8** with Grasshopper
- **.NET Framework 4.8** (typically included with Visual Studio)
- **Visual Studio 2019+** or **JetBrains Rider** (optional, for development)

## Build & Install

All commands should be run from this directory (`grasshopper-plugin/`):

### Option 1: Command Line Build

```bash
# Build the plugin
dotnet build -c Release

# Install to Grasshopper Libraries folder
Copy-Item "bin\Release\net48\LiveCodingGH.gha" "$env:APPDATA\Grasshopper\Libraries\"
```

### Option 2: Visual Studio Build

1. Open `grasshopper_live_bridge.sln` in Visual Studio
2. Set build configuration to **Release**
3. Build the solution (Ctrl+Shift+B)
4. Copy the `.gha` file from `bin\Release\net48\` to your Grasshopper Libraries folder

### Grasshopper Libraries Folder Locations

- **Windows**: `%APPDATA%\Grasshopper\Libraries\`
- **Mac**: `~/Library/Application Support/McNeel/Rhinoceros/MacPlugIns/Grasshopper/Libraries/`

## Usage in Grasshopper

1. **Restart Rhino** after installing the plugin
2. **Open Grasshopper**
3. **Add the component**: 
   - Go to **Params → Util**
   - Find **"Live Coding Controller (Python)"**
   - Place it on your canvas
4. **WebSocket server starts automatically** on port 8181

## WebSocket API

Once the component is on your canvas, it provides a WebSocket server at:

```
ws://localhost:8181/live
```

### Available Actions

**Get Canvas Info:**
```json
{
  "action": "get_canvas_info",
  "correlationId": "unique_id",
  "payload": {}
}
```

**Response**: Complete canvas definition with all components, connections, and data previews.

## Testing the Plugin

After installation, test the WebSocket connection:

```bash
cd ../scripts
npm install
node test-connection.js ping
node test-connection.js get_canvas_info --wait=5000
```

## Project Structure

```
grasshopper-plugin/
├── grasshopper_live_bridge.sln    # Visual Studio solution file
├── LiveCodingGH/                  # Main project folder
│   ├── LiveCodingGH.csproj        # C# project file
│   ├── [Component source files]   # C# implementation
│   └── bin/                       # Build outputs
└── README.md                      # This file
```

## Development

### Building from Source

1. Clone this repository
2. Open `grasshopper_live_bridge.sln` in Visual Studio
3. Restore NuGet packages if needed
4. Build in **Release** mode

### Dependencies

The project references:
- **Grasshopper SDK** (via NuGet)
- **Rhino SDK** (via NuGet)
- **.NET Framework 4.8**

### Debugging

For development and debugging:
1. Build in **Debug** mode
2. Copy the debug `.gha` to Libraries folder
3. Attach Visual Studio debugger to Rhino process
4. Set breakpoints in your component code

## Troubleshooting

### Plugin Not Loading
- Ensure Rhino is completely closed before copying the `.gha` file
- Check that the file is not blocked (right-click → Properties → Unblock)
- Verify .NET Framework 4.8 is installed

### WebSocket Connection Issues
- Make sure no other application is using port 8181
- Check Windows Firewall settings
- Ensure the component is placed on the Grasshopper canvas

### Build Errors
- Verify you have .NET Framework 4.8 SDK installed
- Check that Grasshopper and Rhino SDK packages are properly restored
- Try cleaning and rebuilding the solution

## Next Steps

After successfully building and installing:

1. **Setup MCP Server**: Follow instructions in `../mcp-server/README.md`
2. **Test Scripts**: Use utilities in `../scripts/README.md`
3. **AI Integration**: Configure Claude Code with the MCP server