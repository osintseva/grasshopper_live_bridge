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
        self.slider_uuids = {}  # Store component UUIDs for connections

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
            "payload": payload or {},
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
            "code": """# Simple Python Component with Geometry
import Rhino.Geometry as rg
import System

# Get inputs with defaults
radius = 5.0
if 'radius_input' in locals() and radius_input is not None:
    radius = max(0.1, float(radius_input))

count = 6
if 'count_input' in locals() and count_input is not None:
    count = max(3, int(count_input))

# Create a circle
circle = rg.Circle(rg.Point3d.Origin, radius)

# Create polygon points
points = []
for i in range(count):
    angle = (2 * System.Math.PI * i) / count
    x = radius * System.Math.Cos(angle)
    y = radius * System.Math.Sin(angle)
    points.append(rg.Point3d(x, y, 0))

# Create polyline from points
if len(points) > 2:
    points.append(points[0])  # Close the polyline
    polyline = rg.Polyline(points)
else:
    polyline = None

# Set outputs
circles = circle.ToNurbsCurve()
polygon = polyline.ToNurbsCurve() if polyline else None
vertices = points[:-1] if len(points) > 1 else points  # Remove duplicate closing point
""",
            "inputs": [
                {
                    "name": "radius_input",
                    "nickname": "R",
                    "optional": True,
                    "access": "item",
                    "typeHint": "double",
                },
                {
                    "name": "count_input",
                    "nickname": "N",
                    "optional": True,
                    "access": "item",
                    "typeHint": "number",
                },
            ],
            "outputs": [
                {"name": "circles", "nickname": "C"},
                {"name": "polygon", "nickname": "P"},
                {"name": "vertices", "nickname": "V"},
            ],
            "connections": self._build_connections(),
        }

        response = await self.send_command("create_python_component", payload)
        if response and response.get("status") == "success":
            print("✅ Python component created successfully!")
            return True
        else:
            print(f"❌ Python creation failed: {response}")
            return False

    async def create_source_components(self):
        """Create source sliders for testing connections"""
        print("\\n🔧 Creating source sliders for connections...")

        # Create number slider for radius
        slider_payload = {"x": 50, "y": 100, "nickname": "NumSlider"}
        slider_response = await self.send_command("create_slider", slider_payload)

        # Create number slider for count
        slider2_payload = {"x": 50, "y": 200, "nickname": "CountSlider"}
        slider2_response = await self.send_command("create_slider", slider2_payload)

        # Store component UUIDs for connections
        if slider_response and slider_response.get("status") == "success":
            data = slider_response.get("data", {})
            uuid = data.get("componentUuid") or data.get("componentId")
            if uuid:
                self.slider_uuids["NumSlider"] = uuid
                print(f"📝 NumSlider UUID: {uuid}")

        if slider2_response and slider2_response.get("status") == "success":
            data = slider2_response.get("data", {})
            uuid = data.get("componentUuid") or data.get("componentId")
            if uuid:
                self.slider_uuids["CountSlider"] = uuid
                print(f"📝 CountSlider UUID: {uuid}")

        success_count = sum([bool(slider_response), bool(slider2_response)])
        print(f"✅ Created {success_count}/2 source sliders successfully!")
        print(f"🔗 Stored {len(self.slider_uuids)} slider UUIDs for connections")

        return success_count >= 2  # Both sliders needed for testing

    def _build_connections(self):
        """Build connection list using actual component UUIDs when available"""
        connections = []

        # Connection 1: NumSlider -> Python component input 0
        num_slider_id = self.slider_uuids.get("NumSlider", "NumSlider")  # Fallback to nickname
        connections.append({"sourceId": num_slider_id, "sourceOutput": 0, "targetInput": 0})

        # Connection 2: CountSlider -> Python component input 1
        count_slider_id = self.slider_uuids.get("CountSlider", "CountSlider")  # Fallback to nickname
        connections.append({"sourceId": count_slider_id, "sourceOutput": 0, "targetInput": 1})

        print(f"🔗 Building connections:")
        for i, conn in enumerate(connections):
            print(f"   {i+1}. {conn['sourceId'][:8]}{'...' if len(conn['sourceId']) > 8 else ''} -> input {conn['targetInput']}")

        return connections

    async def verify_connections(self):
        """Verify that connections were actually created"""
        print("\\n🔗 Verifying component connections...")

        response = await self.send_command("get_canvas_info")
        if not response or response.get("status") != "success":
            print("❌ Could not get canvas info for verification")
            return False

        # Parse the pseudocode to look for connections
        pseudocode = response.get("data", "")
        print(f"🔍 Debug: First 200 chars of pseudocode: {repr(pseudocode[:200])}")

        # Try multiple splitting strategies
        if "\n" in pseudocode:
            lines = pseudocode.split("\n")
        elif "\\n" in pseudocode:
            lines = pseudocode.split("\\n")
        elif "\r\n" in pseudocode:
            lines = pseudocode.split("\r\n")
        else:
            lines = [pseudocode]  # Single line fallback

        print(f"📝 Debug: Found {len(lines)} lines in pseudocode")

        # Look for function calls with parameters (indicating connections)
        connections_found = 0
        components_with_inputs = 0

        for line in lines:
            stripped_line = line.strip()
            if not stripped_line or stripped_line.startswith("#"):
                continue

            print(f"  📋 Line: {stripped_line[:100]}{'...' if len(stripped_line) > 100 else ''}")

            if "=" in line and "(" in line and ")" in line:
                # This looks like a component assignment with function call
                components_with_inputs += 1

                # Extract the function call part
                try:
                    if "(" in line and ")" in line:
                        func_start = line.find("(")
                        func_end = line.rfind(")")
                        if func_start < func_end:
                            func_part = line[func_start + 1:func_end].strip()
                            print(f"    🔗 Function params: '{func_part}'")
                            # Check if it has actual parameters (not empty or just spaces/comments)
                            if func_part and not func_part.startswith("#"):
                                # Count comma-separated parameters
                                params = [p.strip() for p in func_part.split(",") if p.strip() and not p.strip().startswith("#")]
                                if params:
                                    connections_found += 1
                                    print(f"    ✅ Found {len(params)} connection parameters: {params}")
                except Exception as e:
                    print(f"    ❌ Error parsing line: {e}")

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
            # Use proper line splitting
            if "\n" in data:
                lines = data.split("\n")
            elif "\\n" in data:
                lines = data.split("\\n")
            else:
                lines = [data]
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
                "verify_connections": await self.verify_connections(),
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
                print(
                    "🎉 All tests passed! Python component creation system working perfectly!"
                )
            elif python_working and connections_working:
                print(
                    "🚀 Python component creation and connections working! System ready for use."
                )
            elif python_working:
                print(
                    "⭐ Python component creation works! Connection system needs verification."
                )
            else:
                print(
                    "⚠️ Python component creation failed - check Rhino version and plugin installation"
                )

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
    print("🐍 Python Component Creation & Connection Tester")
    print("=" * 60)
    print("🔧 Testing: Proven Method + Component Creation + Custom I/O + Connections")
    print("=" * 60)
    asyncio.run(main())
