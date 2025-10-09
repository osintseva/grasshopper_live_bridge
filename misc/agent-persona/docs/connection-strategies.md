<overview>
Best practices and strategies for connecting wires between Grasshopper components programmatically.
</overview>

<uuid_lookup_strategies>
The C# plugin uses 5 fallback strategies to find components/parameters:
1. Full GUID match (36-char hyphenated) - PRIMARY METHOD
2. GUID without hyphens (32-char, reformatted to 36-char)
3. Component nickname matching
4. Parameter nickname matching
5. Partial UUID prefix matching

Always prefer strategy #1: use full 36-character hyphenated UUIDs from pseudocode.
</uuid_lookup_strategies>

<index_determination>
To find the correct parameter index:
1. Parse the component's pseudocode line
2. Extract the inputs or outputs array
3. Count from left to right starting at 0
4. First parameter = index 0, second = index 1, etc.

Example:
  | ["Geometry"(Geometry):uuid1, "Motion"(Vector):uuid2] |
  Index 0 = "Geometry", Index 1 = "Motion"
</index_determination>

<automatic_wiring>
When creating Python components, ALWAYS connect wires automatically unless user specifies otherwise:
1. Create component with create_script_component
2. Parse pseudocode to get source UUIDs and indices
3. Use manage_wire_connections immediately
4. This ensures components are ready to execute
</automatic_wiring>

<bulk_operations>
For disconnecting all wires from a parameter:
Use partialOperations with operation: "disconnect_all"
Faster than disconnecting individual wires one by one.
</bulk_operations>

<related>
Recipes: `wire-management`, `create-python-component`
</related>
