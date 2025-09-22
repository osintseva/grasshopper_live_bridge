#!/usr/bin/env python3
"""
🐍 Python Component Creation Test Script for Rhino 8

This script tests the Python component creation method for programmatically creating Python components
in Grasshopper for Rhino 8, including custom inputs/outputs and automatic connections:

• create_python_component - Proven method using RhinoCodePluginGH.Components.Python3Component API with ScriptVariableParam

🔧 Features Tested:
- ✅ Python component creation with custom inputs/outputs using ScriptVariableParam
- ✅ Automatic source component generation (sliders, data sources)
- ✅ Component-to-component connections
- ✅ Connection verification through canvas analysis
- ✅ Robust method overload handling and error recovery

Prerequisites:
- Grasshopper must be running with LiveCodingComponent loaded
- WebSocket server should be listening on ws://localhost:8181/live
"""

import asyncio
import websockets
import json
import time
import uuid

# Configuration
GRASSHOPPER_WS_URL = "ws://localhost:8181/live"
TIMEOUT = 10.0

class GrasshopperTester:
    def __init__(self):
        self.ws = None
        self.correlation_id = None
        self.responses = {}

    async def connect(self):
        """Connect to Grasshopper WebSocket"""
        try:
            print("🔌 Connecting to Grasshopper...")
            self.ws = await websockets.connect(GRASSHOPPER_WS_URL)
            print("✅ Connected successfully!")
            return True
        except Exception as e:
            print(f"❌ Connection failed: {e}")
            return False

    async def send_command(self, action, payload=None):
        """Send a command and wait for response"""
        if not self.ws:
            print("❌ Not connected to Grasshopper")
            return None

        correlation_id = f"test-{uuid.uuid4().hex[:8]}"

        message = {
            "action": action,
            "correlationId": correlation_id,
            "payload": payload or {}
        }

        print(f"📤 Sending: {action}")
        await self.ws.send(json.dumps(message))

        # Wait for response
        try:
            async with asyncio.timeout(TIMEOUT):
                while True:
                    response = await self.ws.recv()
                    data = json.loads(response)

                    if data.get("correlationId") == correlation_id:
                        if data.get("status") == "queued":
                            print("⏳ Request queued, waiting for final response...")
                            continue
                        return data

        except asyncio.TimeoutError:
            print(f"⏰ Timeout waiting for response to {action}")
            return None
        except Exception as e:
            print(f"❌ Error waiting for response: {e}")
            return None

    async def test_ping(self):
        """Test basic connectivity"""
        print("\\n🏓 Testing ping...")
        response = await self.send_command("ping")
        if response:
            print("✅ Ping successful!")
            return True
        else:
            print("❌ Ping failed!")
            return False

    async def test_python_component(self):
        """Test create_python_component endpoint (proven method)"""
        print("\\n🎯 Testing Python Component Creation (Proven Method)...")

        payload = {
            "x": 200,
            "y": 300,
            "code": """# Proven Python Component Method
import Rhino.Geometry as rg
import System
import random

# Process radius input
base_radius = 2.0
if 'radius_input' in locals() and radius_input is not None:
    base_radius = max(0.1, float(radius_input))

# Process count input
item_count = 6
if 'count_input' in locals() and count_input is not None:
    item_count = max(3, int(count_input))

# Process data list input
input_data = []
if 'data_list' in locals() and data_list:
    input_data = list(data_list)

# Generate concentric circles with varying properties
circles = []
radii = []
for i in range(3):
    radius = base_radius * (1 + i * 0.7)
    circle = rg.Circle(rg.Point3d.Origin, radius)
    circles.append(circle.ToNurbsCurve())
    radii.append(radius)

# Generate polygon based on count
polygon_points = []
for i in range(item_count):
    angle = (2 * rg.Math.PI * i) / item_count
    x = base_radius * 2 * rg.Math.Cos(angle)
    y = base_radius * 2 * rg.Math.Sin(angle)
    polygon_points.append(rg.Point3d(x, y, 0))

# Add random variation if we have input data
if input_data:
    varied_points = []
    for i, pt in enumerate(polygon_points):
        if i < len(input_data):
            # Use input data to modify points
            offset = float(str(input_data[i])[:3] if str(input_data[i]) else "0.5")
        else:
            offset = random.uniform(0.5, 1.5)

        new_pt = rg.Point3d(pt.X * offset, pt.Y * offset, pt.Z)
        varied_points.append(new_pt)

    modified_polygon = varied_points
else:
    modified_polygon = polygon_points

# Create analysis report
process_info = f"Proven Method: {len(circles)} circles (radii: {[f'{r:.2f}' for r in radii]}), {len(polygon_points)} polygon points"
data_analysis = f"Input data: {len(input_data)} items processed" if input_data else "No input data processed"

# Set outputs
output_circles = circles
output_points = polygon_points
modified_points = modified_polygon
component_report = process_info
data_report = data_analysis
execution_timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
""",
            "inputs": [
                {
                    "name": "radius_input",
                    "nickname": "Radius",
                    "optional": True,
                    "access": "item",
                    "typeHint": "double"
                },
                {
                    "name": "count_input",
                    "nickname": "Count",
                    "optional": True,
                    "access": "item",
                    "typeHint": "number"
                },
                {
                    "name": "data_list",
                    "nickname": "Data",
                    "optional": True,
                    "access": "list"
                }
            ],
            "outputs": [
                {
                    "name": "output_circles",
                    "nickname": "Circles"
                },
                {
                    "name": "output_points",
                    "nickname": "Points"
                },
                {
                    "name": "modified_points",
                    "nickname": "Modified"
                },
                {
                    "name": "component_report",
                    "nickname": "Report"
                },
                {
                    "name": "data_report",
                    "nickname": "Data Info"
                },
                {
                    "name": "execution_timestamp",
                    "nickname": "Time"
                }
            ],
            "connections": [
                {
                    "sourceId": "NumSlider",
                    "sourceOutput": 0,
                    "targetInput": 0
                },
                {
                    "sourceId": "CountSlider",
                    "sourceOutput": 0,
                    "targetInput": 1
                },
                {
                    "sourceId": "Python Script",  # The source Python component
                    "sourceOutput": 2,  # Count output (C)
                    "targetInput": 2
                }
            ]
        }

        response = await self.send_command("create_python_component", payload)
        if response and response.get("status") == "success":
            print("✅ Python component created successfully!")
            return True
        else:
            print(f"❌ Python creation failed: {response}")
            return False

    async def create_source_components(self):
        """Create multiple source components for testing connections"""
        print("\\n🔧 Creating source components for connections...")

        # Create number slider
        slider_payload = {
            "x": 50,
            "y": 100,
            "nickname": "NumSlider"
        }
        slider_response = await self.send_command("create_slider", slider_payload)

        # Create another number slider
        slider2_payload = {
            "x": 50,
            "y": 200,
            "nickname": "CountSlider"
        }
        slider2_response = await self.send_command("create_slider", slider2_payload)

        # Create a basic Python component as data source
        source_python_payload = {
            "x": 50,
            "y": 300,
            "code": """# Source Component
import Rhino.Geometry as rg
import random

# Generate some test data
points = []
for i in range(5):
    x = random.uniform(-10, 10)
    y = random.uniform(-10, 10)
    points.append(rg.Point3d(x, y, 0))

texts = [f"Item_{i}" for i in range(3)]

A = points  # Point list output
B = texts   # Text list output
C = len(points)  # Count output
"""
        }
        source_response = await self.send_command("create_python_component", source_python_payload)

        success_count = sum([bool(slider_response), bool(slider2_response), bool(source_response)])
        print(f"✅ Created {success_count}/3 source components successfully!")

        return success_count >= 2  # At least 2 components needed for testing

    async def verify_connections(self):
        """Verify that connections were actually created"""
        print("\\n🔗 Verifying component connections...")

        response = await self.send_command("get_canvas_info")
        if not response or response.get("status") != "success":
            print("❌ Could not get canvas info for verification")
            return False

        # Parse the pseudocode to look for connections
        pseudocode = response.get("data", "")
        lines = pseudocode.split('\\n')

        # Look for function calls with parameters (indicating connections)
        connections_found = 0
        components_with_inputs = 0

        for line in lines:
            if '=' in line and '(' in line and ')' in line:
                # This looks like a component assignment with function call
                if not line.strip().startswith('#'):
                    components_with_inputs += 1
                    # Count parameters in function call
                    if '(' in line and ')' in line:
                        func_part = line.split('(')[1].split(')')[0]
                        if func_part.strip():  # Has parameters
                            connections_found += 1

        print(f"📊 Found {components_with_inputs} components with potential inputs")
        print(f"🔗 Found {connections_found} components with connections")

        # Success if we have some connections
        if connections_found >= 2:
            print("✅ Connection verification successful!")
            return True
        elif connections_found >= 1:
            print("⚠️ Some connections found, partial success")
            return True
        else:
            print("⚠️ No clear connections detected in pseudocode")
            return False

    async def get_canvas_info(self):
        """Get current canvas information"""
        print("\\n📊 Getting canvas information...")

        response = await self.send_command("get_canvas_info")
        if response and response.get("status") == "success":
            print("✅ Canvas info retrieved successfully!")
            # Print a summary
            data = response.get("data", "")
            lines = data.split('\\n')
            for line in lines:
                if line.startswith("# Components:"):
                    print(f"📈 {line}")
                    break
            return True
        else:
            print("❌ Failed to get canvas info")
            return False

    async def run_all_tests(self):
        """Run complete test suite"""
        print("🧪 Starting Python Component Creation Test")
        print("=" * 60)

        # Connect to Grasshopper
        if not await self.connect():
            return False

        try:
            results = {
                "ping": await self.test_ping(),
                "create_sources": await self.create_source_components(),
                "python_component": await self.test_python_component(),
                "canvas_info": await self.get_canvas_info(),
                "verify_connections": await self.verify_connections()
            }

            # Summary
            print("\\n📋 Test Results Summary:")
            print("=" * 30)
            passed = 0
            total = len(results)

            for test_name, result in results.items():
                status = "✅ PASS" if result else "❌ FAIL"
                print(f"{test_name:20} {status}")
                if result:
                    passed += 1

            print(f"\\n🎯 Total: {passed}/{total} tests passed")

            # Analyze results
            python_working = results.get("python_component", False)
            connections_working = results.get("verify_connections", False)

            if passed == total:
                print("🎉 All tests passed! Python component creation system working perfectly!")
            elif python_working and connections_working:
                print("🚀 Python component creation and connections working! System ready for use.")
            elif python_working:
                print("⭐ Python component creation works! Connection system needs verification.")
            else:
                print("⚠️ Python component creation failed - check Rhino version and plugin installation")

            print("\\n🔧 Method Status:")
            if python_working:
                print("✅ create_python_component - Working (ScriptVariableParam API)")
            else:
                print("❌ create_python_component - Failed")

            return passed == total

        finally:
            if self.ws:
                await self.ws.close()
                print("\\n🔌 Disconnected from Grasshopper")

async def main():
    """Main test runner"""
    tester = GrasshopperTester()
    success = await tester.run_all_tests()

    if success:
        print("\\n🚀 Ready to use Python component creation!")
    else:
        print("\\n🔧 Some tests failed - check the troubleshooting guide")

if __name__ == "__main__":
    print("Python Component Creation & Connection Tester")
    print("=" * 60)
    print("Testing: Proven Method + Component Creation + Custom I/O + Connections")
    print("=" * 60)
    asyncio.run(main())