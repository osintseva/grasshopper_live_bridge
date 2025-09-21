#!/usr/bin/env python3
"""
🐍 Test Script for Rhino 8 Python Component Creation

This script tests all three methods for programmatically creating Python components
in Grasshopper for Rhino 8, including:
1. create_python_script - Original basic method (existing functionality)
2. create_python_advanced - Using official Python3Component.Create API (Rhino 8.14+)
3. create_python_xml - Using XML serialization workaround (fallback)

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
            "x": 100,
            "y": 100,
            "code": """# Advanced Python Test Component
import Rhino.Geometry as rg
import System

# Process input data
results = []
if 'numbers' in locals() and numbers:
    for i, num in enumerate(numbers):
        result = num * 2
        results.append(result)

if 'text_input' in locals() and text_input:
    message = f"Processed {len(text_input)} text items"
else:
    message = "No text input received"

# Set outputs
doubled_numbers = results
status_message = message
current_time = System.DateTime.Now.ToString()
""",
            "inputs": [
                {
                    "name": "numbers",
                    "nickname": "nums",
                    "optional": True,
                    "access": "list"
                },
                {
                    "name": "text_input",
                    "nickname": "txt",
                    "optional": True,
                    "access": "item"
                }
            ],
            "outputs": [
                {
                    "name": "doubled_numbers",
                    "nickname": "doubled"
                },
                {
                    "name": "status_message",
                    "nickname": "status"
                },
                {
                    "name": "current_time",
                    "nickname": "time"
                }
            ],
            "connections": [
                # We'll try to connect to any existing number slider or panel
                {
                    "sourceId": "From VSCode",  # Default slider nickname
                    "sourceOutput": 0,
                    "targetInput": 0
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
            "code": """# Basic Python Script Component
import Rhino.Geometry as rg
import datetime

# Simple processing
current_time = datetime.datetime.now()
message = f"Basic Python executed at {current_time.strftime('%H:%M:%S')}"

# Create a simple point
pt = rg.Point3d(0, 0, 0)

# Output variables (standard naming for basic components)
A = message
B = pt
"""
        }

        response = await self.send_command("create_python_script", payload)
        if response:
            print("✅ Basic Python script component created successfully!")
            return True
        else:
            print(f"❌ Basic Python creation failed: {response}")
            return False

    async def test_xml_python(self):
        """Test create_python_xml endpoint"""
        print("\n📋 Testing XML-based Python Component Creation...")

        payload = {
            "x": 300,
            "y": 100,
            "code": """# XML-based Python Test Component
import Rhino.Geometry as rg
import math

# Create some geometry based on inputs
points = []
if 'count' in locals() and count:
    num_points = int(count) if count > 0 else 5
else:
    num_points = 5

if 'radius' in locals() and radius:
    r = float(radius) if radius > 0 else 10.0
else:
    r = 10.0

# Generate points in a circle
for i in range(num_points):
    angle = (2 * math.pi * i) / num_points
    x = r * math.cos(angle)
    y = r * math.sin(angle)
    pt = rg.Point3d(x, y, 0)
    points.append(pt)

# Create a polyline
if len(points) > 2:
    points.append(points[0])  # Close the polyline
    polyline = rg.Polyline(points)
    curve = polyline.ToNurbsCurve()
else:
    curve = None

# Set outputs
output_points = points[:-1]  # Exclude duplicate closing point
output_curve = curve
output_info = f"Created {len(output_points)} points in circle with radius {r}"
""",
            "inputs": [
                {
                    "name": "count",
                    "nickname": "N"
                },
                {
                    "name": "radius",
                    "nickname": "R"
                }
            ],
            "outputs": [
                {
                    "name": "output_points",
                    "nickname": "pts"
                },
                {
                    "name": "output_curve",
                    "nickname": "crv"
                },
                {
                    "name": "output_info",
                    "nickname": "info"
                }
            ],
            "connections": [
                # Try to connect to existing slider
                {
                    "sourceId": "From VSCode",
                    "sourceOutput": 0,
                    "targetInput": 0
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

    async def create_test_slider(self):
        """Create a test slider for connections"""
        print("\n🎚️ Creating test slider for connections...")

        payload = {
            "x": 50,
            "y": 150,
            "nickname": "TestSlider"
        }

        response = await self.send_command("create_slider", payload)
        if response:
            print("✅ Test slider created!")
            return True
        else:
            print("❌ Failed to create test slider")
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
                "create_slider": await self.create_test_slider(),
                "basic_python": await self.test_basic_python(),
                "advanced_python": await self.test_advanced_python(),
                "xml_python": await self.test_xml_python(),
                "canvas_info": await self.get_canvas_info()
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

            if passed == total:
                print("🎉 All tests passed! All three Python creation methods are working.")
            elif results["advanced_python"] and results["xml_python"] and results["basic_python"]:
                print("🚀 All Python creation methods work! You have full Rhino 8 compatibility.")
            elif results["advanced_python"]:
                print("✨ Advanced method works - you have Rhino 8.14+ API available!")
            elif results["xml_python"]:
                print("⚙️ XML method works - using fallback approach")
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
    print("🐍 Rhino 8 Python Component Creation Tester")
    print("=" * 50)
    asyncio.run(main())