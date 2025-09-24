#!/usr/bin/env python3
"""
📄 Canvas State Saver Script

This script fetches the current Grasshopper canvas state via the LiveCoding plugin
WebSocket connection and saves it to a markdown file for documentation and analysis.

Features:
- ✅ Connect to Grasshopper via WebSocket
- ✅ Fetch current canvas pseudocode
- ✅ Save to timestamped .md file
- ✅ Format with proper markdown structure

Prerequisites:
- Grasshopper must be running with LiveCodingComponent loaded
- WebSocket server should be listening on ws://localhost:8181/live
"""

import asyncio
import websockets
import json
import uuid
from datetime import datetime
from pathlib import Path

# Configuration
GRASSHOPPER_WS_URL = "ws://localhost:8181/live"
TIMEOUT = 10.0


class CanvasStateSaver:
    def __init__(self):
        self.ws = None

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

        correlation_id = f"canvas-state-{uuid.uuid4().hex[:8]}"

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

    async def get_canvas_state(self):
        """Get current canvas state from Grasshopper"""
        print("📊 Fetching canvas state...")

        response = await self.send_command("get_canvas_info")
        if response and response.get("status") == "success":
            print("✅ Canvas state retrieved successfully!")
            return response.get("data", "")
        else:
            print(f"❌ Failed to get canvas state: {response}")
            return None

    def save_to_markdown(self, canvas_data):
        """Save canvas data to a markdown file"""
        if not canvas_data:
            print("❌ No canvas data to save")
            return False

        # Generate timestamp for filename
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        filename = f"canvas_state_{timestamp}.md"

        # Get the script directory
        script_dir = Path(__file__).parent
        filepath = script_dir / filename

        # Create markdown content
        markdown_content = f"""# Grasshopper Canvas State
*Generated on: {datetime.now().strftime("%Y-%m-%d %H:%M:%S")}*

## Canvas Pseudocode

```
{canvas_data}
```

## Analysis Summary

This canvas state was captured from Grasshopper via the LiveCoding plugin WebSocket connection.

### Key Information
- **Generated**: {datetime.now().strftime("%Y-%m-%d %H:%M:%S")}
- **Source**: Grasshopper LiveCoding Plugin
- **Format**: Enhanced Pipe-Delimited Pseudocode

### Usage
This pseudocode representation can be used for:
- Canvas documentation
- Component analysis
- Workflow understanding
- Debugging and troubleshooting
"""

        try:
            with open(filepath, 'w', encoding='utf-8') as f:
                f.write(markdown_content)

            print(f"✅ Canvas state saved to: {filepath}")
            print(f"📄 File size: {filepath.stat().st_size} bytes")
            return True

        except Exception as e:
            print(f"❌ Failed to save file: {e}")
            return False

    async def run(self):
        """Main execution flow"""
        print("📄 Canvas State Saver")
        print("=" * 50)

        # Connect to Grasshopper
        if not await self.connect():
            return False

        try:
            # Get canvas state
            canvas_data = await self.get_canvas_state()

            if canvas_data:
                # Save to markdown file
                success = self.save_to_markdown(canvas_data)

                if success:
                    print("\n🎉 Canvas state successfully saved to markdown file!")
                    return True
                else:
                    print("\n❌ Failed to save canvas state")
                    return False
            else:
                print("\n❌ Could not retrieve canvas state")
                return False

        finally:
            if self.ws:
                await self.ws.close()
                print("\n🔌 Disconnected from Grasshopper")


async def main():
    """Main entry point"""
    saver = CanvasStateSaver()
    success = await saver.run()

    if success:
        print("\n🚀 Canvas state saved successfully!")
    else:
        print("\n🔧 Failed to save canvas state - check connection and try again")


if __name__ == "__main__":
    print("📄 Grasshopper Canvas State Saver")
    print("=" * 50)
    print("🔧 Fetching canvas state from Grasshopper LiveCoding plugin...")
    print("=" * 50)
    asyncio.run(main())