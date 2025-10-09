<overview>
Step-by-step workflow for creating a Python component with custom inputs/outputs and automatic wire connections.
</overview>

<prerequisites>
- Canvas state retrieved via `get_canvas_state`
- Target connection points identified from pseudocode
</prerequisites>

<steps>
1. Design Component Structure
   - Define required inputs and outputs
   - Write Python code using RhinoCommon API
   - For list outputs use DataTree: `import Grasshopper as gh; output = gh.DataTree[object](); output.AddRange(data, gh.Kernel.Data.GH_Path(0))`

2. Create Component
   - Use `create_script_component` with code, inputs[], outputs[], position
   - Component will be placed at specified (x, y) coordinates

3. Connect Wires (DO AUTOMATICALLY unless user specifies otherwise)
   - Parse pseudocode to extract source component UUIDs and output indices
   - Use `manage_wire_connections` immediately after creation
   - Connect all required inputs

4. Verify Success (ALWAYS DO THIS)
   - Use `get_component_info` with returned component UUID
   - Check `runtimeMessages` array for errors/warnings
   - If runtimeMessages is empty or has only remarks, component is working

5. Fix Errors If Found
   - If errors exist, use `search_rhinocommon_docs` to find correct API
   - Recreate component with fixed code
   - Reconnect wires
   - Verify again until no errors remain
</steps>

<related>
Docs: `pseudocode-format`, `connection-strategies`
</related>
