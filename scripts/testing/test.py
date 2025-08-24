import Rhino.Geometry as rg
import random
import math
from Grasshopper import DataTree
from Grasshopper.Kernel.Data import GH_Path

# Set random seed for reproducibility (optional)
random.seed(42)

# Create 20 random points within 20m radius from origin (0,0,0)
points = []
circles = []

for i in range(20):
    # Generate random angle (0 to 2π)
    angle = random.uniform(0, 2 * math.pi)
    
    # Generate random radius (0 to 20m)
    radius = random.uniform(0, 20)
    
    # Convert polar to cartesian coordinates
    x = radius * math.cos(angle)
    y = radius * math.sin(angle)
    z = 0  # Keep points on XY plane
    
    # Create point
    point = rg.Point3d(x, y, z)
    points.append(point)
    
    # Create circle at this point with random radius (1.0 to 3.0)
    circle_radius = random.uniform(1.0, 3.0)
    
    # Create vertical plane at the point for the circle
    plane = rg.Plane(point, rg.Vector3d.ZAxis)
    
    # Create circle
    circle = rg.Circle(plane, circle_radius)
    circles.append(circle)

# Convert circles to curves for output
curves = [circle.ToNurbsCurve() for circle in circles]

# Output as DataTree
tree = DataTree[rg.Curve]()
for i, curve in enumerate(curves):
    tree.Add(curve, GH_Path(0))

a = tree