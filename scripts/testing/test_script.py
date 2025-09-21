#!/usr/bin/env python3
"""
🐍 Test Script for Rhino 8 Python Component Creation with Connections

This comprehensive script tests all three methods for programmatically creating Python components
in Grasshopper for Rhino 8, including custom inputs/outputs and automatic connections:

1. create_python_script - Original basic method (existing functionality)
2. create_python_advanced - Using official Python3Component.Create API (Rhino 8.14+) with custom parameters
3. create_python_xml - Using XML serialization workaround (fallback) with custom parameters

🔧 Features Tested:
- ✅ Component creation with custom inputs/outputs
- ✅ Automatic source component generation (sliders, data sources)
- ✅ Component-to-component connections
- ✅ Connection verification through canvas analysis
- ✅ Cross-method compatibility testing

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
        print("\n🏓 Testing ping...")
        response = await self.send_command("ping")
        if response:
            print("✅ Ping successful!")
            return True
        else:
            print("❌ Ping failed!")
            return False

    async def test_advanced_python(self):
        """Test create_python_advanced endpoint"""
        print("\n🚀 Testing Advanced Python Component Creation (Rhino 8.14+ API)...")

        payload = {
            "x": 300,
            "y": 100,
            "code": """# Advanced Python Test Component
import Rhino.Geometry as rg
import System
import math

# Process numerical input
processed_numbers = []
if 'radius' in locals() and radius is not None:
    base_radius = float(radius)
    # Create multiple circles with varying radii
    for i in range(5):
        r = base_radius * (i + 1) * 0.5
        processed_numbers.append(r)
else:
    processed_numbers = [1.0, 2.0, 3.0]

# Process count input
num_points = 8
if 'point_count' in locals() and point_count is not None:
    num_points = max(3, int(point_count))

# Process point data if available
transformed_points = []
if 'input_points' in locals() and input_points:
    for pt in input_points:
        if hasattr(pt, 'X'):  # Check if it's a Point3d
            # Transform points by scaling
            new_pt = rg.Point3d(pt.X * 1.5, pt.Y * 1.5, pt.Z)
            transformed_points.append(new_pt)

# Generate output geometry
circles = []
for r in processed_numbers[:3]:  # Limit to 3 circles
    circle = rg.Circle(rg.Point3d.Origin, r)
    circles.append(circle.ToNurbsCurve())

# Create polygon based on point count
polygon_points = []
for i in range(num_points):
    angle = (2 * math.pi * i) / num_points
    x = 5.0 * math.cos(angle)
    y = 5.0 * math.sin(angle)
    polygon_points.append(rg.Point3d(x, y, 0))

# Set outputs
output_circles = circles
output_points = polygon_points
transformed_inputs = transformed_points
status_report = f"Advanced Python: {len(circles)} circles, {len(polygon_points)} polygon points, {len(transformed_points)} transformed points"
execution_time = System.DateTime.Now.ToString()
""",
            "inputs": [
                {
                    "name": "radius",
                    "nickname": "R",
                    "optional": True,
                    "access": "item"
                },
                {
                    "name": "point_count",
                    "nickname": "N",
                    "optional": True,
                    "access": "item"
                },
                {
                    "name": "input_points",
                    "nickname": "Pts",
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
                    "name": "transformed_inputs",
                    "nickname": "XPts"
                },
                {
                    "name": "status_report",
                    "nickname": "Status"
                },
                {
                    "name": "execution_time",
                    "nickname": "Time"
                }
            ],
            "connections": [
                # Connect to the created source components
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
                    "sourceOutput": 0,  # Points output (A)
                    "targetInput": 2
                }
            ]
        }

        response = await self.send_command("create_python_advanced", payload)
        if response and response.get("status") == "success":
            print("✅ Advanced Python component created successfully!")
            return True
        else:
            print(f"❌ Advanced Python creation failed: {response}")
            return False

    async def test_basic_python(self):
        """Test create_python_script endpoint (existing basic method)"""
        print("\n🐙 Testing Basic Python Script Creation (Original Method)...")

        payload = {
            "x": 500,
            "y": 100,
            "code": """# Basic Python Script Component with Inputs
import Rhino.Geometry as rg
import datetime

# Access inputs (x and y should be available as default inputs)
num_value = x if 'x' in locals() and x is not None else 5.0
count_value = y if 'y' in locals() and y is not None else 10

# Process data
current_time = datetime.datetime.now()
message = f"Basic Python at {current_time.strftime('%H:%M:%S')} with inputs: num={num_value}, count={count_value}"

# Create geometry based on inputs
points = []
for i in range(int(count_value)):
    angle = (2 * 3.14159 * i) / count_value
    x_coord = float(num_value) * rg.Math.Cos(angle)
    y_coord = float(num_value) * rg.Math.Sin(angle)
    points.append(rg.Point3d(x_coord, y_coord, 0))

# Output variables (standard naming for basic components)
A = message
B = points
C = f"Created {len(points)} points with radius {num_value}"
"""
        }

        response = await self.send_command("create_python_script", payload)
        if response:
            print("✅ Basic Python script component created successfully!")
            # Note: Basic method doesn't support connection specification in payload
            # Connections would need to be made manually or via separate endpoint
            return True
        else:
            print(f"❌ Basic Python creation failed: {response}")
            return False

    async def test_xml_python(self):
        """Test create_python_xml endpoint"""
        print("\n📋 Testing XML-based Python Component Creation...")

        payload = {
            "x": 500,
            "y": 200,
            "code": """# XML-based Python Test Component
import Rhino.Geometry as rg
import math

# Process numerical inputs
scale_factor = 1.0
if 'scale' in locals() and scale is not None:
    scale_factor = max(0.1, float(scale))

sides = 6
if 'polygon_sides' in locals() and polygon_sides is not None:
    sides = max(3, int(polygon_sides))

# Process text inputs
text_items = []
if 'text_data' in locals() and text_data:
    if isinstance(text_data, list):
        text_items = text_data
    else:
        text_items = [str(text_data)]

# Generate geometry
# Create regular polygon
polygon_points = []
radius = 5.0 * scale_factor
for i in range(sides):
    angle = (2 * math.pi * i) / sides
    x = radius * math.cos(angle)
    y = radius * math.sin(angle)
    polygon_points.append(rg.Point3d(x, y, 0))

# Close the polygon
if len(polygon_points) > 2:
    polygon_points.append(polygon_points[0])
    polyline = rg.Polyline(polygon_points)
    polygon_curve = polyline.ToNurbsCurve()
else:
    polygon_curve = None

# Create concentric circles
circles = []
for i in range(3):
    r = radius * (0.3 + i * 0.35)
    circle = rg.Circle(rg.Point3d.Origin, r)
    circles.append(circle.ToNurbsCurve())

# Create text analysis
text_analysis = f"Processed {len(text_items)} text items: {', '.join(text_items[:3])}"
if len(text_items) > 3:
    text_analysis += f" (and {len(text_items)-3} more)"

# Set outputs
geometry_points = polygon_points[:-1]  # Exclude duplicate closing point
geometry_curves = [polygon_curve] + circles if polygon_curve else circles
text_report = text_analysis
component_info = f"XML Python: {sides}-sided polygon, scale {scale_factor:.2f}, {len(circles)} circles"
stats = {
    'points': len(geometry_points),
    'curves': len(geometry_curves),
    'scale': scale_factor,
    'sides': sides
}
""",
            "inputs": [
                {
                    "name": "scale",
                    "nickname": "Scale"
                },
                {
                    "name": "polygon_sides",
                    "nickname": "Sides"
                },
                {
                    "name": "text_data",
                    "nickname": "Text"
                }
            ],
            "outputs": [
                {
                    "name": "geometry_points",
                    "nickname": "Points"
                },
                {
                    "name": "geometry_curves",
                    "nickname": "Curves"
                },
                {
                    "name": "text_report",
                    "nickname": "Report"
                },
                {
                    "name": "component_info",
                    "nickname": "Info"
                },
                {
                    "name": "stats",
                    "nickname": "Stats"
                }
            ],
            "connections": [
                # Connect to the created source components
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
                    "sourceOutput": 1,  # Text output (B)
                    "targetInput": 2
                }
            ]
        }

        response = await self.send_command("create_python_xml", payload)
        if response and response.get("status") == "success":
            print("✅ XML-based Python component created successfully!")
            return True
        else:
            print(f"❌ XML Python creation failed: {response}")
            return False

    async def create_source_components(self):
        """Create multiple source components for testing connections"""
        print("\n🔧 Creating source components for connections...")

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
        source_response = await self.send_command("create_python_script", source_python_payload)

        success_count = sum([bool(slider_response), bool(slider2_response), bool(source_response)])
        print(f"✅ Created {success_count}/3 source components successfully!")

        return success_count >= 2  # At least 2 components needed for testing

    async def verify_connections(self):
        """Verify that connections were actually created"""
        print("\n🔗 Verifying component connections...")

        response = await self.send_command("get_canvas_info")
        if not response or response.get("status") != "success":
            print("❌ Could not get canvas info for verification")
            return False

        # Parse the pseudocode to look for connections
        pseudocode = response.get("data", "")
        lines = pseudocode.split('\n')

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
        print("\n📊 Getting canvas information...")

        response = await self.send_command("get_canvas_info")
        if response and response.get("status") == "success":
            print("✅ Canvas info retrieved successfully!")
            # Print a summary
            data = response.get("data", "")
            lines = data.split('\n')
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
        print("🧪 Starting Rhino 8 Python Component Creation Tests")
        print("=" * 60)

        # Connect to Grasshopper
        if not await self.connect():
            return False

        try:
            results = {
                "ping": await self.test_ping(),
                "create_sources": await self.create_source_components(),
                "basic_python": await self.test_basic_python(),
                "advanced_python": await self.test_advanced_python(),
                "xml_python": await self.test_xml_python(),
                "canvas_info": await self.get_canvas_info(),
                "verify_connections": await self.verify_connections()
            }

            # Summary
            print("\n📋 Test Results Summary:")
            print("=" * 30)
            passed = 0
            total = len(results)

            for test_name, result in results.items():
                status = "✅ PASS" if result else "❌ FAIL"
                print(f"{test_name:20} {status}")
                if result:
                    passed += 1

            print(f"\n🎯 Total: {passed}/{total} tests passed")

            # Analyze Python creation results
            python_methods = ["basic_python", "advanced_python", "xml_python"]
            python_passed = sum(1 for method in python_methods if results.get(method, False))
            connections_working = results.get("verify_connections", False)

            print(f"\n🐍 Python Creation Summary: {python_passed}/3 methods working")
            if connections_working:
                print("🔗 Component connections: ✅ Working")
            else:
                print("🔗 Component connections: ⚠️ Issues detected")

            if passed == total:
                print("🎉 All tests passed! Complete Python component creation system working with connections!")
            elif python_passed == 3 and connections_working:
                print("🚀 All Python creation methods work with connections! You have full Rhino 8 compatibility.")
            elif python_passed == 3:
                print("⭐ All Python creation methods work! Connection system needs verification.")
            elif results["advanced_python"] and results["xml_python"]:
                print("✨ Advanced & XML methods work - excellent Rhino 8 compatibility!")
            elif results["advanced_python"]:
                print("🆕 Advanced method works - you have Rhino 8.14+ API available!")
            elif results["xml_python"]:
                print("⚙️ XML method works - using fallback approach for older Rhino 8")
            elif results["basic_python"]:
                print("🔧 Basic method works - original functionality available")
            else:
                print("⚠️ All Python creation methods failed - check Rhino version and plugin installation")

            return passed == total

        finally:
            if self.ws:
                await self.ws.close()
                print("\n🔌 Disconnected from Grasshopper")

async def main():
    """Main test runner"""
    tester = GrasshopperTester()
    success = await tester.run_all_tests()

    if success:
        print("\n🚀 Ready to use programmatic Python component creation!")
    else:
        print("\n🔧 Some tests failed - check the troubleshooting guide")

if __name__ == "__main__":
    print("🐍 Rhino 8 Python Component Creation & Connection Tester")
    print("=" * 60)
    print("🔧 Testing: Component Creation + Custom I/O + Connections")
    print("=" * 60)
    asyncio.run(main())