# GHPython Coding Hints & Best Practices

## Overview
This document collects coding hints and best practices discovered during development to ensure reliable Python code generation for Grasshopper components. These hints should be incorporated into prompts for better code generation results.

---

## Data Output Patterns

### ✅ Use DataTree for List Outputs
**Issue**: Simple Python lists may not display properly in Grasshopper outputs.

**Solution**: Wrap list outputs in Grasshopper DataTree structure.

```python
from Grasshopper import DataTree
from Grasshopper.Kernel.Data import GH_Path
import Rhino.Geometry as rg

# Instead of: A = curves (simple list)
# Use DataTree:
tree = DataTree[rg.Curve]()
for i, curve in enumerate(curves):
    tree.Add(curve, GH_Path(0))
A = tree
```

**When to use**: 
- Multiple geometry objects (curves, surfaces, meshes)
- Lists that need to be consumed by other Grasshopper components
- Any collection that should maintain proper data structure in GH

---

## Geometry Creation Patterns

### ✅ Always Specify Plane for Circles
**Issue**: Circles without proper plane context may not orient correctly.

**Solution**: Create explicit planes for circle geometry.

```python
import Rhino.Geometry as rg

# Create vertical plane at the point for the circle
plane = rg.Plane(point, rg.Vector3d.ZAxis)
circle = rg.Circle(plane, radius)
```

### ✅ Convert Geometry to Curves When Needed
**Issue**: Some Rhino geometry types need conversion for proper Grasshopper output.

**Solution**: Use appropriate conversion methods.

```python
# Convert circles to curves for output compatibility
curves = [circle.ToNurbsCurve() for circle in circles]
```

---

## Import Patterns

### ✅ Standard GHPython Imports
Always include these imports for geometry-heavy scripts:

```python
import Rhino.Geometry as rg
from Grasshopper import DataTree
from Grasshopper.Kernel.Data import GH_Path
import random
import math
```

---

## Random Number Generation

### ✅ Set Random Seed for Reproducibility
**Issue**: Random results change on every execution, making debugging difficult.

**Solution**: Set a fixed seed for consistent results during development.

```python
import random
random.seed(42)  # Use consistent seed for debugging
```

---

## Coordinate System Patterns

### ✅ Use Polar Coordinates for Radial Distribution
**Issue**: Random X,Y coordinates don't create uniform distribution within radius.

**Solution**: Generate polar coordinates then convert to Cartesian.

```python
import math
import random

# Generate uniform distribution within radius
angle = random.uniform(0, 2 * math.pi)
radius = random.uniform(0, max_radius)

# Convert to Cartesian
x = radius * math.cos(angle)  
y = radius * math.sin(angle)
z = 0  # Usually keep on XY plane
```

---

## Output Variable Naming

### ✅ Use Standard Grasshopper Output Names
**Pattern**: Use single uppercase letters for outputs: `A`, `B`, `C`, etc.

```python
# Standard output pattern
A = primary_result
B = secondary_result  
C = debug_info
```

---

## Error Prevention Patterns

### ✅ Initialize Collections Before Loops
**Issue**: Undefined variables cause runtime errors.

**Solution**: Always initialize lists/collections before use.

```python
# Initialize before loop
points = []
circles = []

for i in range(count):
    # populate collections
    points.append(point)
    circles.append(circle)
```

---

## Performance Patterns

### ✅ Use List Comprehensions for Simple Transformations
**Pattern**: Prefer list comprehensions for simple data transformations.

```python
# Efficient transformation
curves = [circle.ToNurbsCurve() for circle in circles]

# Instead of:
# curves = []
# for circle in circles:
#     curves.append(circle.ToNurbsCurve())
```

---

## Template for Multi-Geometry Output

```python
import Rhino.Geometry as rg
from Grasshopper import DataTree
from Grasshopper.Kernel.Data import GH_Path
import random
import math

# Set seed for reproducibility
random.seed(42)

# Initialize collections
geometry_objects = []

# ... geometry generation logic ...

# Convert to appropriate types
curves = [obj.ToNurbsCurve() for obj in geometry_objects]

# Output as DataTree
tree = DataTree[rg.Curve]()
for i, curve in enumerate(curves):
    tree.Add(curve, GH_Path(0))

A = tree
```

---

## Notes for Prompt Engineering

**Include in prompts**:
1. "Always output geometry collections using Grasshopper DataTree structure"
2. "Use standard Grasshopper imports: Rhino.Geometry, DataTree, GH_Path"
3. "Set random.seed() for reproducible results during development"
4. "Convert geometry objects to appropriate types (ToNurbsCurve(), etc.)"
5. "Use single uppercase letters for output variables (A, B, C)"

**Common patterns to mention**:
- Polar coordinate generation for radial distributions
- Explicit plane creation for oriented geometry
- List comprehensions for transformations
- Proper collection initialization