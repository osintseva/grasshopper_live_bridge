# Grasshopper Live Bridge - Project Description

## Overview
This is a three-tier system for live coding and AI-driven design in Grasshopper, enabling real-time Python code editing and execution within Grasshopper 3D.

## Architecture Components

### 1. Grasshopper Component (`/grasshopper_component/LiveCodingGH/`)
- **File**: `LiveCodingComponent.cs`
- **Purpose**: WebSocket server running inside Grasshopper
- **Port**: 8181 (WebSocket endpoint: `ws://localhost:8181/live`)
- **Functionality**:
  - Hosts WebSocket server for receiving commands
  - Processes command queue with timer (100ms intervals)
  - Can create/update Python scripts in Grasshopper canvas
  - Can create sliders and other components
  - Uses reflection to handle different GHPython versions

### 2. MCP Server (`/mcp_server/`)
- **Status**: Coming soon (AI orchestration layer)

### 3. Node.js Scripts (`/scripts/`)
- **Main Script**: `test_connection.js` - WebSocket client for testing
- **Test File**: `testing/test_script.py` - Simple Python script for testing

## Key Features

### WebSocket Commands Supported:
- `ping` - Test connection
- `create_python_script` - Creates new Python component in Grasshopper
- `create_slider` - Creates number slider component
- `update_script` - Updates existing Python component code

### Python Integration:
- Supports multiple GHPython versions through reflection
- Default fallback creates instruction panel if GHPython not found
- Can load Python code from external files

## File Structure
```
grasshopper_live_bridge/
├── README.md
├── package.json (WebSocket dependencies)
├── grasshopper_component/LiveCodingGH/
│   ├── LiveCodingComponent.cs (Main C# component)
│   └── LiveCodingGH.csproj
├── mcp_server/ (placeholder)
├── scripts/
│   ├── test_connection.js (WebSocket test client)
│   └── testing/test_script.py (test Python code)
└── docs/ & examples/ (documentation)
```

## Development Workflow
1. Build and install Grasshopper component (.gha file)
2. Place component in Grasshopper canvas
3. Use Node.js scripts to send commands
4. Python scripts are created/updated in real-time

## Current Status
- Grasshopper component: Functional with WebSocket server
- Test script: Working for creating Python components
- MCP server: Not implemented yet

## Usage Examples
- Create Python script: `node scripts/test_connection.js create_python_script scripts/testing/test_script.py`
- Test connection: `node scripts/test_connection.js ping`
- Create slider: `node scripts/test_connection.js create_slider`

## Dependencies
- Node.js packages: `ws` (WebSocket)
- C# libraries: WebSocketSharp, Newtonsoft.Json
- Grasshopper SDK
- GHPython plugin (for Python execution)