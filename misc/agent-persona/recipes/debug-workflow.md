<overview>
Step-by-step workflow for diagnosing and fixing runtime errors in Grasshopper components.
</overview>

<prerequisites>
- Component UUID identified (from canvas or error report)
- Access to `get_component_info` and `search_rhinocommon_docs` tools
</prerequisites>

<steps>
1. Inspect Component
   - Use `get_component_info` with component UUID
   - Read `runtimeMessages` array for error details
   - Identify error type (TypeError, AttributeError, etc.)

2. Analyze Error Message
   - Extract API/method name from error
   - Identify parameter mismatches or type issues
   - Note expected vs actual types

3. Search Documentation
   - Use `search_rhinocommon_docs` with relevant keywords from error
   - Find correct method signature and parameter types
   - Review examples in documentation

4. Fix Code
   - Correct API usage based on documentation
   - Fix type conversions if needed
   - Ensure DataTree usage for list outputs

5. Recreate Component
   - Use `create_script_component` with corrected code
   - Reconnect wires using `manage_wire_connections`
   - Verify with `get_component_info` - check that runtimeMessages is clear
</steps>

<related>
Recipes: `create-python-component`
</related>
