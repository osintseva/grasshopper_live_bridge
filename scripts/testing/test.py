# scripts/testing/test.py
import Rhino.Geometry as rg
import random
import math

# If N is not connected, provide a default value
if 'N' not in globals() or N is None:
    N = 20

points = []
_circles = [] # Use a different name to avoid conflict with output param

for i in range(N):
    angle = random.uniform(0, 2 * math.pi)
    radius = random.uniform(0, 20)
    x = radius * math.cos(angle)
    y = radius * math.sin(angle)
    point = rg.Point3d(x, y, 0)
    points.append(point)

    circle_radius = random.uniform(1.0, 3.0)
    plane = rg.Plane(point, rg.Vector3d.ZAxis)
    circle = rg.Circle(plane, circle_radius)
    _circles.append(circle)

# Assign the final list to the named output 'circles'
circles = _circles

# THE FIX: Also assign a value to the default 'out' parameter.
# This tells the component the script ran successfully.
out = "Generated {} circles.".format(len(circles))