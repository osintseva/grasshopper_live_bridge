<overview>
Step-by-step workflow for connecting and disconnecting wires between Grasshopper components.
</overview>

<prerequisites>
- Source and target component UUIDs identified from pseudocode
- Parameter indices determined (zero-indexed)
</prerequisites>

<steps>
1. Parse Pseudocode
   - Locate source component line
   - Find output parameter index (0-indexed in outputs array)
   - Locate target component line
   - Find input parameter index (0-indexed in inputs array)

2. Build Connection Payload
   - Create connections array with:
     - sourceComponentUuid (36-char hyphenated UUID)
     - sourceOutputIndex (integer, 0-based)
     - targetComponentUuid (36-char hyphenated UUID)
     - targetInputIndex (integer, 0-based)

3. Execute Wire Operation
   - Use `manage_wire_connections` with action: "connect" or "disconnect"
   - For bulk disconnection, use partialOperations with "disconnect_all"

4. Verify Connections
   - Use `get_canvas_state` to see updated pseudocode
   - Connected parameters will show in inputs/outputs arrays
   - Disconnected parameters are still shown but not connected
</steps>

<related>
Docs: `pseudocode-format`, `connection-strategies`
</related>
