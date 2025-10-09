<format>
Enhanced Pipe-Delimited with Types:
  variable|x,y|comp_uuid: ComponentType = "Component Name" | [inputs] | [outputs]
</format>

<examples>
slider_1|100,200|a1b2c3d4-e5f6-7890-...: Number = "Height" | [] | ["Number"(Double):out_uuid]
move_1|300,200|e5f6g7h8-i9j0-k1l2-...: Transform = "Move" | ["Geometry"(Geometry):in1_uuid, "Motion"(Vector):in2_uuid] | ["Geometry"(Geometry):out1_uuid]
python_comp|400,250|m3n4o5p6-q7r8-s9t0-...: Python = "My Script" | ["curves"(Curve):in_uuid] | ["result"(Object):out_uuid]
</examples>

<uuid_format>
- Always 36 characters with hyphens: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
- Lowercase in JavaScript, any case in C#
- Used for wire connection targeting in manage_wire_connections
- Each component has one UUID, each parameter has one UUID
</uuid_format>

<connection_indices>
Inputs and outputs are ZERO-INDEXED arrays.
First input = index 0, second input = index 1, etc.

Example: move_1 above has:
- Input index 0: "Geometry" parameter
- Input index 1: "Motion" parameter
- Output index 0: "Geometry" parameter
</connection_indices>

<related>
Recipes: `create-python-component`, `wire-management`
</related>
