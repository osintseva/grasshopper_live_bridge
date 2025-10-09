<overview>
Grasshopper components categorized by their role in the definition, useful for conversion and analysis tasks.
</overview>

<parameter_components>
Components that provide input values:
- Number Slider: Numeric input with min/max/value
- Panel: Text input
- Point: 3D point coordinates
- Vector: Direction/displacement vectors
- Boolean Toggle: True/false switches
</parameter_components>

<geometry_components>
Components that create or modify geometry:
- Circle, Rectangle, Polygon: Basic 2D shapes
- Box, Sphere, Cylinder: Basic 3D primitives
- Extrude, Loft, Sweep: Surface creation
- Move, Rotate, Scale: Transformations
- Boolean operations: Union, Difference, Intersection
</geometry_components>

<python_components>
Custom scripted components:
- Python3Component (RhinoCode): Modern Python scripting
- GhPython (legacy): Older Python component type
- C# Script: C# scripting support

Identified by ComponentType "Python" or similar in pseudocode.
</python_components>

<data_components>
Components that manipulate data structures:
- List operations: Item, Split, Merge, Flatten
- Tree operations: Path, Graft, Simplify
- Mathematical: Addition, Multiplication, Range, Series
</data_components>

<related>
Roles: `converter` uses this for mapping components to code constructs
</related>
