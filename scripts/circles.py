# Self-managing IronPython script for Grasshopper
# This script creates its own inputs and outputs on first run

import Rhino.Geometry as rg
import System
import System.Math as math
import Grasshopper as gh
from Grasshopper import DataTree
from Grasshopper.Kernel.Data import GH_Path

# Check if this is the first run (inputs don't exist yet)
if "N" not in globals() or "R" not in globals():
    # Clear existing parameters
    ghenv.Component.Params.Input.Clear()
    ghenv.Component.Params.Output.Clear()
    
    # Create input parameters
    param_n = gh.Kernel.Parameters.Param_Integer()
    param_n.Name = "N"
    param_n.NickName = "N"
    param_n.Description = "Number of circles"
    param_n.Access = gh.Kernel.GH_ParamAccess.item
    param_n.Optional = True
    ghenv.Component.Params.Input.Add(param_n)
    
    param_r = gh.Kernel.Parameters.Param_Number()
    param_r.Name = "R"
    param_r.NickName = "R"
    param_r.Description = "Radius of circles"
    param_r.Access = gh.Kernel.GH_ParamAccess.item
    param_r.Optional = True
    ghenv.Component.Params.Input.Add(param_r)
    
    # Create output parameters
    param_circles = gh.Kernel.Parameters.Param_Curve()
    param_circles.Name = "circles"
    param_circles.NickName = "circles"
    param_circles.Description = "Generated circles"
    param_circles.Access = gh.Kernel.GH_ParamAccess.list
    ghenv.Component.Params.Output.Add(param_circles)
    
    param_status = gh.Kernel.Parameters.Param_GenericObject()
    param_status.Name = "status"
    param_status.NickName = "status"
    param_status.Description = "Status message"
    param_status.Access = gh.Kernel.GH_ParamAccess.item
    ghenv.Component.Params.Output.Add(param_status)
    
    # Expire solution to update the component
    ghenv.Component.ExpireSolution(True)
    
    # Set default values
    N = 10
    R = 1.0
    circles = []
    status = "Component initialized - connect inputs"

else:
    # Main script execution when inputs are available
    
    # Use .NET Random for IronPython
    _random = System.Random()
    
    # Read inputs safely
    try:
        N_value = int(N) if N is not None else 10
    except:
        N_value = 10
    
    try:
        R_value = float(R) if R is not None else 1.0
    except:
        R_value = 1.0
    
    # Create circles with clustering behavior
    _circles = []
    
    if N_value > 0 and R_value > 0:
        # First circle at origin
        center = rg.Point3d(0, 0, 0)
        plane = rg.Plane(center, rg.Vector3d.ZAxis)
        _circles.append(rg.Circle(plane, R_value))
        
        # Generate remaining circles close to existing ones
        for i in range(1, N_value):
            # Pick a random existing circle as reference
            ref_idx = _random.Next(len(_circles))
            ref_circle = _circles[ref_idx]
            ref_center = ref_circle.Center
            
            # Generate position near reference circle
            angle = _random.NextDouble() * 2.0 * math.PI
            
            # Distance: between touching (2R) and 6R away
            min_dist = R_value * 2.0
            max_dist = R_value * 6.0
            distance = min_dist + _random.NextDouble() * (max_dist - min_dist)
            
            # Calculate new position
            x = ref_center.X + distance * math.Cos(angle)
            y = ref_center.Y + distance * math.Sin(angle)
            z = ref_center.Z
            
            # Create new circle
            new_center = rg.Point3d(x, y, z)
            new_plane = rg.Plane(new_center, rg.Vector3d.ZAxis)
            _circles.append(rg.Circle(new_plane, R_value))
    
    # Output circles as curves (better for GH compatibility)
    circles = [c.ToNurbsCurve() for c in _circles]
    
    # Status message
    status = "Generated {} circles with radius {}".format(len(_circles), R_value)