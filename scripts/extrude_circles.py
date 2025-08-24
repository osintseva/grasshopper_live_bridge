# Self-managing IronPython script for extruding circles into breps
# This script creates its own inputs and outputs on first run

import Rhino.Geometry as rg
import System
import Grasshopper as gh

# Check if this is the first run (inputs don't exist yet)
if "circles" not in globals() or "H" not in globals():
    # Clear existing parameters
    ghenv.Component.Params.Input.Clear()
    ghenv.Component.Params.Output.Clear()
    
    # Create input parameters
    param_circles = gh.Kernel.Parameters.Param_Curve()
    param_circles.Name = "circles"
    param_circles.NickName = "circles"
    param_circles.Description = "Circle curves to extrude"
    param_circles.Access = gh.Kernel.GH_ParamAccess.list
    param_circles.Optional = True
    ghenv.Component.Params.Input.Add(param_circles)
    
    param_h = gh.Kernel.Parameters.Param_Number()
    param_h.Name = "H"
    param_h.NickName = "H"
    param_h.Description = "Extrusion height"
    param_h.Access = gh.Kernel.GH_ParamAccess.item
    param_h.Optional = True
    ghenv.Component.Params.Input.Add(param_h)
    
    # Create output parameters
    param_breps = gh.Kernel.Parameters.Param_Brep()
    param_breps.Name = "breps"
    param_breps.NickName = "breps"
    param_breps.Description = "Extruded breps"
    param_breps.Access = gh.Kernel.GH_ParamAccess.list
    ghenv.Component.Params.Output.Add(param_breps)
    
    param_out = gh.Kernel.Parameters.Param_GenericObject()
    param_out.Name = "out"
    param_out.NickName = "out"
    param_out.Description = "Status message"
    param_out.Access = gh.Kernel.GH_ParamAccess.item
    ghenv.Component.Params.Output.Add(param_out)
    
    # Expire solution to update the component
    ghenv.Component.ExpireSolution(True)
    
    # Set default values
    circles = []
    H = 10.0
    breps = []
    out = "Component initialized - connect inputs"

else:
    # Main script execution when inputs are available
    
    # Read inputs safely
    try:
        input_circles = circles if circles is not None else []
    except:
        input_circles = []
        
    try:
        H_value = float(H) if H is not None else 10.0
    except:
        H_value = 10.0
    
    # Create breps by extruding circles
    result_breps = []
    
    print("DEBUG: Processing {} input circles".format(len(input_circles)))
    print("DEBUG: Height value: {}".format(H_value))
    
    for i, circle_curve in enumerate(input_circles):
        print("DEBUG: Processing circle {}".format(i))
        
        if circle_curve is None:
            print("DEBUG: Circle {} is None, skipping".format(i))
            continue
            
        try:
            print("DEBUG: Circle {} type: {}".format(i, type(circle_curve).__name__))
            
            # Get circle from curve
            circle = None
            if hasattr(circle_curve, 'TryGetCircle'):
                print("DEBUG: Circle {} has TryGetCircle method".format(i))
                success, circle = circle_curve.TryGetCircle()
                print("DEBUG: TryGetCircle result: success={}, circle={}".format(success, circle))
                if not success:
                    print("DEBUG: Failed to get circle from curve {}".format(i))
                    continue
            elif hasattr(circle_curve, 'Center') and hasattr(circle_curve, 'Radius'):
                print("DEBUG: Circle {} already has Center/Radius properties".format(i))
                circle = circle_curve
            else:
                print("DEBUG: Circle {} doesn't have expected properties".format(i))
                continue
                
            if circle is None:
                print("DEBUG: Circle {} is None after extraction".format(i))
                continue
                
            # Get circle properties
            center = circle.Center
            radius = circle.Radius
            print("DEBUG: Circle {} - Center: ({}, {}, {}), Radius: {}".format(
                i, center.X, center.Y, center.Z, radius))
            
            if radius <= 0:
                print("DEBUG: Circle {} has invalid radius: {}".format(i, radius))
                continue
                
            if H_value <= 0:
                print("DEBUG: Invalid height: {}".format(H_value))
                continue
                
            # Try multiple methods to create the brep
            brep = None
            
            # Method 1: Direct cylinder creation
            try:
                plane = rg.Plane(center, rg.Vector3d.ZAxis)
                print("DEBUG: Created plane for circle {}".format(i))
                
                cylinder = rg.Cylinder(plane, H_value, radius)
                print("DEBUG: Created cylinder for circle {} - Height: {}, Radius: {}".format(
                    i, H_value, radius))
                
                brep = cylinder.ToBrep(True, True)
                print("DEBUG: Method 1 - ToBrep result for circle {}: {}".format(i, brep))
                
                if brep and brep.IsValid:
                    print("DEBUG: Method 1 success for circle {}".format(i))
                else:
                    print("DEBUG: Method 1 failed for circle {}".format(i))
                    brep = None
            except Exception as ex1:
                print("DEBUG: Method 1 exception for circle {}: {}".format(i, str(ex1)))
                brep = None
            
            # Method 2: Surface extrusion from curve
            if brep is None:
                try:
                    print("DEBUG: Trying Method 2 - surface extrusion for circle {}".format(i))
                    # Create extrusion vector
                    vector = rg.Vector3d(0, 0, H_value)
                    
                    # Try to extrude the curve directly
                    if hasattr(circle_curve, 'ToNurbsCurve'):
                        curve_to_extrude = circle_curve.ToNurbsCurve()
                    else:
                        curve_to_extrude = circle_curve
                        
                    # Create surface by extrusion
                    surface = rg.Surface.CreateExtrusion(curve_to_extrude, vector)
                    if surface:
                        brep = surface.ToBrep()
                        print("DEBUG: Method 2 - surface extrusion result: {}".format(brep))
                        
                        if brep and brep.IsValid:
                            print("DEBUG: Method 2 success for circle {}".format(i))
                        else:
                            brep = None
                    else:
                        print("DEBUG: Method 2 - CreateExtrusion returned None for circle {}".format(i))
                        
                except Exception as ex2:
                    print("DEBUG: Method 2 exception for circle {}: {}".format(i, str(ex2)))
                    brep = None
            
            # Check final result
            if brep is None:
                print("DEBUG: All methods failed for circle {}".format(i))
                continue
                
            if not brep.IsValid:
                print("DEBUG: Final brep for circle {} is not valid".format(i))
                continue
                
            result_breps.append(brep)
            print("DEBUG: Successfully added brep for circle {}".format(i))
                
        except Exception as ex:
            print("DEBUG: Exception processing circle {}: {}".format(i, str(ex)))
            continue
    
    # Set outputs
    breps = result_breps
    out = "Extruded {} circles into {} breps (height: {})".format(
        len(input_circles), len(result_breps), H_value)