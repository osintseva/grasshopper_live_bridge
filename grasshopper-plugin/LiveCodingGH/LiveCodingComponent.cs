using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Parameters;
using GH_IO.Serialization;
using Newtonsoft.Json;
using Rhino.Geometry;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace LiveCoding
{
    /// <summary>
    /// Live-coding controller for Python (Rhino 7 RhinoCode + legacy GhPython fallback).
    /// WebSocket: ws://localhost:8181/live
    /// Actions:
    ///   - ping
    ///   - create_slider                      { x?, y?, nickname? }
    ///   - create_python_script               { x?, y?, code? }
    ///   - update_script                      { componentId, code }
    ///   - get_canvas_info                    (returns JSON of entire canvas definition)
    /// </summary>
    public class LiveCodingComponent : GH_Component
    {
        private const int WS_PORT = 8181;

        private WebSocketServer _server;
        private readonly System.Collections.Concurrent.ConcurrentQueue<CommandMessage> _queue =
            new System.Collections.Concurrent.ConcurrentQueue<CommandMessage>();
        private Timer _pump;

        public LiveCodingComponent()
            : base("Live Coding Controller (Python)", "LivePy",
                   "WebSocket controller for live coding Python in Grasshopper",
                   "Params", "Util")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager) { }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "S", "Server status", GH_ParamAccess.item);
            pManager.AddTextParameter("Last Command", "C", "Last received command", GH_ParamAccess.item);
            pManager.AddTextParameter("Debug Log", "D", "Debug information", GH_ParamAccess.list);
        }

        private string LastCommand { get; set; } = "—";
        private readonly List<string> DebugLog = new List<string>();

        private void LogDebug(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var logEntry = $"[{timestamp}] {message}";
            DebugLog.Add(logEntry);

            // Keep only last 20 entries
            if (DebugLog.Count > 20)
                DebugLog.RemoveAt(0);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (_server == null)
            {
                StartServer();
                StartPump();
            }

            var status = _server != null && _server.IsListening
                ? $"Server: ws://localhost:{WS_PORT}/live (OK)"
                : "Server not running";

            DA.SetData(0, status);
            DA.SetData(1, LastCommand);
            DA.SetDataList(2, DebugLog);
        }

        // ---------------------- WebSocket bootstrap ----------------------

        private void StartServer()
        {
            try
            {
                _server = new WebSocketServer(WS_PORT);
#pragma warning disable CS0618
                _server.AddWebSocketService("/live", () =>
                {
                    return new LiveCodingService(_queue, LogDebug);
                }); // obsolete API is fine for POC
#pragma warning restore CS0618

                Task.Run(() =>
                {
                    _server.Start();
                });

                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"WS started on ws://localhost:{WS_PORT}/live");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"WS start failed: {ex.Message}");
            }
        }

        private void StartPump()
        {
            _pump = new Timer { Interval = 50 };
            _pump.Tick += (_, __) =>
            {
                if (_queue.TryDequeue(out var msg))
                {
                    try
                    {
                        LastCommand = $"{msg.Action} @ {DateTime.Now:HH:mm:ss}";
                        LogDebug($"Processing message: {msg.Action}");
                        Execute(msg);
                        LogDebug($"Message processing completed for: {msg.Action}");
                        ExpireSolution(true);
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"Message processing exception: {ex.GetType().Name} - {ex.Message}");
                        LogDebug($"Stack trace: {ex.StackTrace}");
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Command failed: {ex.Message}");
                    }
                }
            };
            _pump.Start();
            LogDebug("Message pump started");
        }

        // ---------------------- Command dispatcher ----------------------

        private void Execute(CommandMessage cmd)
        {
            var doc = Grasshopper.Instances.ActiveCanvas?.Document;
            LogDebug($"Execute: ActiveCanvas null? {Grasshopper.Instances.ActiveCanvas == null}, Document null? {doc == null}");

            if (doc == null)
            {
                LogDebug("No active Grasshopper document - command ignored");
                SendErrorResponse(cmd.Action, cmd.CorrelationId, "No active Grasshopper document");
                return;
            }

            var payload = cmd.Payload ?? new Dictionary<string, object>();

            try
            {
                switch ((cmd.Action ?? string.Empty).ToLowerInvariant())
                {
                    case "ping":
                        SendSuccessResponse("ping", cmd.CorrelationId, "Pong!");
                        break;

                    case "create_slider":
                        CreateSlider(doc, payload, cmd.CorrelationId);
                        break;

                    case "create_python_script":
                        CreatePythonScript(doc, payload, cmd.CorrelationId);
                        break;

                    case "update_script":
                        UpdateExistingScript(doc, payload, cmd.CorrelationId);
                        break;

                    case "get_canvas_info":
                        GetCanvasInfo(doc, cmd.CorrelationId);
                        break;

                    case "create_python_advanced":
                        CreatePythonAdvanced(doc, payload, cmd.CorrelationId);
                        break;

                    case "create_python_xml":
                        CreatePythonXml(doc, payload, cmd.CorrelationId);
                        break;

                    default:
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Unknown action: {cmd.Action}");
                        SendErrorResponse(cmd.Action, cmd.CorrelationId, $"Unknown action: {cmd.Action}");
                        break;
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Command execution failed: {ex.Message}");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Command failed: {ex.Message}");
                SendErrorResponse(cmd.Action, cmd.CorrelationId, ex.Message);
            }
        }

        // ---------------------- Utilities ----------------------

        private static float F(object o, float fallback)
        {
            try { return o == null ? fallback : Convert.ToSingle(o); }
            catch { return fallback; }
        }
        private static string S(object o, string fallback = "") => o == null ? fallback : o.ToString();

        private static readonly BindingFlags BF =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.FlattenHierarchy | BindingFlags.IgnoreCase;

        // ---------------------- Response Methods ----------------------

        private void SendSuccessResponse(string action, string correlationId, string message = null)
        {
            try
            {
                var responseDict = new Dictionary<string, object>
                {
                    ["action"] = action + "_response",
                    ["correlationId"] = correlationId ?? Guid.NewGuid().ToString(),
                    ["status"] = "success"
                };

                if (!string.IsNullOrEmpty(message))
                {
                    responseDict["message"] = message;
                }

                string responseJson = JsonConvert.SerializeObject(responseDict);
                LogDebug($"Sending success response for {action}");

                if (!string.IsNullOrEmpty(correlationId))
                {
                    LiveCodingService.SendToSession(correlationId, responseJson);
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to send success response: {ex.Message}");
            }
        }

        private void SendErrorResponse(string action, string correlationId, string error)
        {
            try
            {
                var responseDict = new Dictionary<string, object>
                {
                    ["action"] = action + "_response",
                    ["correlationId"] = correlationId ?? Guid.NewGuid().ToString(),
                    ["status"] = "error",
                    ["message"] = error
                };

                string responseJson = JsonConvert.SerializeObject(responseDict);
                LogDebug($"Sending error response for {action}: {error}");

                if (!string.IsNullOrEmpty(correlationId))
                {
                    LiveCodingService.SendToSession(correlationId, responseJson);
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to send error response: {ex.Message}");
            }
        }

        // ---------------------- Canvas Analysis ----------------------

        private const int PREVIEW_CHAR_LIMIT = 20;
        private const int FULL_DATA_CHAR_LIMIT = 10000;
        private const bool INCLUDE_DATA_PREVIEWS = false; // Set to false to disable inline comments with data previews
        private const int DATA_PREVIEW_LIMIT = 100;
        private const int UUID_LENGTH = 8; // First 8 chars of UUID

        private void GetCanvasInfo(GH_Document doc, string correlationId = null)
        {
            try
            {
                LogDebug($"GetCanvasInfo started. Doc null? {doc == null}");
                if (doc == null)
                {
                    LogDebug("No active Grasshopper document found");
                    return;
                }

                var components = new List<PseudocodeComponent>();
                var docObjects = doc.Objects.ToList();
                var guidToIndexMap = docObjects.Select((obj, i) => new { obj.InstanceGuid, i }).ToDictionary(x => x.InstanceGuid, x => x.i);
                var usedUuids = new HashSet<string>();

                // Parse all components
                for (int i = 0; i < docObjects.Count; i++)
                {
                    var obj = docObjects[i];
                    var comp = new PseudocodeComponent
                    {
                        Id = i,
                        Guid = obj.InstanceGuid
                    };

                    if (obj is IGH_Component ghComponent)
                    {
                        comp.Name = ghComponent.Name;
                        comp.NickName = ghComponent.NickName;
                        comp.Description = ghComponent.Description;
                        comp.IsComponent = true;

                        // Parse inputs
                        foreach (var inputParam in ghComponent.Params.Input)
                        {
                            var connections = new List<int[]>();
                            foreach (var source in inputParam.Sources)
                            {
                                var sourceOwnerObj = source.Attributes.GetTopLevel.DocObject;
                                if (sourceOwnerObj != null && guidToIndexMap.TryGetValue(sourceOwnerObj.InstanceGuid, out int sourceId))
                                {
                                    int sourceParamIndex = (sourceOwnerObj is IGH_Component sourceComp) ? sourceComp.Params.Output.IndexOf(source) : 0;
                                    if (sourceParamIndex > -1) connections.Add(new int[] { sourceId, sourceParamIndex });
                                }
                            }
                            comp.Inputs.Add(new PseudocodeInput { Name = inputParam.Name, TypeName = inputParam.TypeName, Connections = connections });
                        }

                        // Parse outputs
                        comp.Outputs = ghComponent.Params.Output.Select(p => {
                            var data = p.VolatileData.AllData(true);
                            var dataPreview = data.Any() ? string.Join(", ", data.Select(GetCompactDataPreview)) : "null";

                            return new PseudocodeOutput { Name = p.Name, TypeName = p.TypeName, DataPreview = dataPreview, HasRecipients = p.Recipients.Count > 0 };
                        }).ToList();
                    }
                    else if (obj is IGH_Param ghParam)
                    {
                        comp.Name = ghParam.Name;
                        comp.NickName = ghParam.NickName;
                        comp.Description = ghParam.Description;
                        comp.IsComponent = false;

                        var data = ghParam.VolatileData.AllData(true);
                        var dataPreview = data.Any() ? string.Join(", ", data.Select(GetCompactDataPreview)) : "null";

                        comp.Outputs.Add(new PseudocodeOutput { Name = ghParam.Name, TypeName = ghParam.TypeName, DataPreview = dataPreview, HasRecipients = ghParam.Recipients.Count > 0 });

                        if (ghParam.Sources.Any())
                        {
                            var connections = new List<int[]>();
                            foreach (var source in ghParam.Sources)
                            {
                                var sourceOwnerObj = source.Attributes.GetTopLevel.DocObject;
                                if (sourceOwnerObj != null && guidToIndexMap.TryGetValue(sourceOwnerObj.InstanceGuid, out int sourceId))
                                {
                                    int sourceParamIndex = (sourceOwnerObj is IGH_Component sourceComp) ? sourceComp.Params.Output.IndexOf(source) : 0;
                                    if (sourceParamIndex > -1) connections.Add(new int[] { sourceId, sourceParamIndex });
                                }
                            }
                            comp.Inputs.Add(new PseudocodeInput { Name = "Input", TypeName = ghParam.TypeName, Connections = connections });
                        }
                    }

                    components.Add(comp);
                }

                // Generate variable names for all components
                foreach (var comp in components)
                {
                    comp.VariableName = GenerateVariableName(comp, usedUuids);
                }

                // Sort components topologically
                var sortedComponents = TopologicalSort(components);

                // Generate pseudocode
                var output = new StringBuilder();

                // Header
                output.AppendLine("# === GRASSHOPPER DEFINITION PSEUDOCODE ===");
                output.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                var sourceCount = sortedComponents.Count(c => !c.Inputs.Any());
                var sinkCount = sortedComponents.Count(c => !c.Outputs.Any(o => o.HasRecipients));
                var totalConnections = sortedComponents.Sum(c => c.Inputs.Sum(i => i.Connections.Count));

                output.AppendLine($"# Components: {sortedComponents.Count} | Connections: {totalConnections} | Sources: {sourceCount} | Sinks: {sinkCount}");
                output.AppendLine();

                // Source components (no inputs)
                var sources = sortedComponents.Where(c => !c.Inputs.Any()).ToList();
                if (sources.Any())
                {
                    output.AppendLine("# === SOURCE DATA (No inputs) ===");
                    foreach (var comp in sources)
                    {
                        var mainOutput = comp.Outputs.FirstOrDefault();
                        if (mainOutput != null)
                        {
                            string typeName = mainOutput.TypeName ?? "Object";
                            string preview = INCLUDE_DATA_PREVIEWS ? $"  # {mainOutput.DataPreview}" : "";
                            output.AppendLine($"{comp.VariableName}: {typeName} = {comp.Name ?? "Component"}(){preview}");
                        }
                    }
                    output.AppendLine();
                }

                // Main processing chain (components with inputs and outputs)
                var processors = sortedComponents.Where(c => c.Inputs.Any() && c.Outputs.Any(o => o.HasRecipients)).ToList();
                if (processors.Any())
                {
                    output.AppendLine("# === MAIN PROCESSING CHAIN ===");
                    foreach (var comp in processors)
                    {
                        var mainOutput = comp.Outputs.FirstOrDefault();
                        if (mainOutput != null)
                        {
                            string typeName = mainOutput.TypeName ?? "Object";
                            if (comp.Outputs.Count > 1)
                                typeName = $"Tuple[{string.Join(", ", comp.Outputs.Select(o => o.TypeName ?? "Object"))}]";

                            // Generate function call
                            var functionName = comp.Name ?? "Process";
                            var args = new List<string>();

                            foreach (var input in comp.Inputs)
                            {
                                foreach (var connection in input.Connections)
                                {
                                    var sourceComp = sortedComponents.FirstOrDefault(c => c.Id == connection[0]);
                                    if (sourceComp != null)
                                    {
                                        args.Add(sourceComp.VariableName);
                                    }
                                }
                            }

                            if (comp.Outputs.Count == 1)
                            {
                                string preview = INCLUDE_DATA_PREVIEWS ? $"  # {mainOutput.DataPreview}" : "";
                                output.AppendLine($"{comp.VariableName}: {typeName} = {functionName}({string.Join(", ", args)}){preview}");
                            }
                            else
                            {
                                // Multiple outputs
                                var outputNames = string.Join(", ", comp.Outputs.Select(o => $"{comp.VariableName}_{o.Name?.ToLower() ?? "out"}"));
                                output.AppendLine($"{outputNames}: {typeName} = {functionName}({string.Join(", ", args)})");
                            }
                        }
                    }
                    output.AppendLine();
                }

                // Disconnected/Display components (sinks)
                var sinks = sortedComponents.Where(c => c.Outputs.Any() && !c.Outputs.Any(o => o.HasRecipients) && c.Inputs.Any()).ToList();
                if (sinks.Any())
                {
                    output.AppendLine("# === DISCONNECTED/DISPLAY COMPONENTS ===");
                    foreach (var comp in sinks)
                    {
                        var mainOutput = comp.Outputs.FirstOrDefault();
                        if (mainOutput != null)
                        {
                            string typeName = mainOutput.TypeName ?? "Object";
                            var functionName = comp.Name ?? "Display";
                            var args = new List<string>();

                            foreach (var input in comp.Inputs)
                            {
                                foreach (var connection in input.Connections)
                                {
                                    var sourceComp = sortedComponents.FirstOrDefault(c => c.Id == connection[0]);
                                    if (sourceComp != null)
                                    {
                                        args.Add(sourceComp.VariableName);
                                    }
                                }
                            }

                            string preview = INCLUDE_DATA_PREVIEWS ? $"  # {mainOutput.DataPreview}" : "";
                            output.AppendLine($"{comp.VariableName}: {typeName} = {functionName}({string.Join(", ", args)}){preview}");
                        }
                    }
                }

                string pseudocodeResult = output.ToString();
                LogDebug($"Pseudocode generated, length: {pseudocodeResult.Length}");

                // Broadcast the pseudocode via WebSocket
                try
                {
                    BroadcastCanvasInfo(pseudocodeResult, correlationId);
                    LogDebug("Pseudocode sent via WebSocket");
                }
                catch (Exception broadcastEx)
                {
                    LogDebug($"Broadcast failed: {broadcastEx.Message}");
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to get canvas info: {ex.Message}");
            }
        }


        private string GetDataPreviewString(IGH_Goo goo)
        {
            if (goo == null) return "null";

            switch (goo.ScriptVariable())
            {
                case Point3d pt:
                    return $"Pt({pt.X:F2},{pt.Y:F2},{pt.Z:F2})";

                case Line line:
                    return $"Line[({line.From.X:F2},{line.From.Y:F2},{line.From.Z:F2})->({line.To.X:F2},{line.To.Y:F2},{line.To.Z:F2})]";

                case Curve curve when curve.IsLinear():
                    return $"Line[({curve.PointAtStart.X:F2},{curve.PointAtStart.Y:F2},{curve.PointAtStart.Z:F2})->({curve.PointAtEnd.X:F2},{curve.PointAtEnd.Y:F2},{curve.PointAtEnd.Z:F2})]";

                case Curve curve:
                    var nurbsCurve = curve.ToNurbsCurve();
                    if (nurbsCurve != null)
                    {
                        string pointsStr = string.Join(";", nurbsCurve.Points.Select(p => $"({p.Location.X:F2},{p.Location.Y:F2},{p.Location.Z:F2})"));
                        return $"CurvePts[{pointsStr}]";
                    }
                    return $"Curve(L={curve.GetLength():F2})";

                case Brep brep:
                    return $"Brep(V={brep.Vertices.Count},F={brep.Faces.Count})";

                default:
                    return goo.ToString();
            }
        }

        /// <summary>
        /// Generates a compact data preview string (max 100 chars).
        /// </summary>
        private string GetCompactDataPreview(IGH_Goo goo)
        {
            if (goo == null) return "null";

            string preview = "";
            switch (goo.ScriptVariable())
            {
                case Point3d pt:
                    preview = $"Pt({pt.X:F2},{pt.Y:F2},{pt.Z:F2})";
                    break;

                case Line line:
                    preview = $"Line[({line.From.X:F2},{line.From.Y:F2},{line.From.Z:F2})->({line.To.X:F2},{line.To.Y:F2},{line.To.Z:F2})]";
                    break;

                case Curve curve when curve.IsLinear():
                    preview = $"Line[({curve.PointAtStart.X:F2},{curve.PointAtStart.Y:F2},{curve.PointAtStart.Z:F2})->({curve.PointAtEnd.X:F2},{curve.PointAtEnd.Y:F2},{curve.PointAtEnd.Z:F2})]";
                    break;

                case Curve curve:
                    var nurbsCurve = curve.ToNurbsCurve();
                    if (nurbsCurve != null)
                    {
                        string pointsStr = string.Join(";", nurbsCurve.Points.Select(p => $"({p.Location.X:F2},{p.Location.Y:F2},{p.Location.Z:F2})"));
                        preview = $"CurvePts[{pointsStr}]";
                    }
                    else
                    {
                        preview = $"Curve(L={curve.GetLength():F2})";
                    }
                    break;

                case Brep brep:
                    preview = $"Brep(V={brep.Vertices.Count},F={brep.Faces.Count})";
                    break;

                default:
                    preview = goo.ToString();
                    break;
            }

            // Truncate to 100 characters
            if (preview.Length > DATA_PREVIEW_LIMIT)
            {
                preview = preview.Substring(0, DATA_PREVIEW_LIMIT - 3) + "...";
            }

            return preview;
        }

        /// <summary>
        /// Truncates UUID to specified length and handles collisions.
        /// </summary>
        private string GetShortUuid(Guid guid, HashSet<string> usedUuids)
        {
            string uuidStr = guid.ToString("N"); // No hyphens
            for (int len = 4; len <= UUID_LENGTH; len += 2)
            {
                string shortUuid = uuidStr.Substring(0, len);
                if (!usedUuids.Contains(shortUuid))
                {
                    usedUuids.Add(shortUuid);
                    return shortUuid;
                }
            }
            // Fallback if all lengths are taken (very unlikely)
            usedUuids.Add(uuidStr.Substring(0, UUID_LENGTH));
            return uuidStr.Substring(0, UUID_LENGTH);
        }

        /// <summary>
        /// Generates a meaningful variable name from component info.
        /// </summary>
        private string GenerateVariableName(PseudocodeComponent comp, HashSet<string> usedUuids)
        {
            string baseName = "";

            // Priority: NickName > Name > ComponentType
            if (!string.IsNullOrEmpty(comp.NickName))
            {
                baseName = comp.NickName.ToLower().Replace(" ", "_").Replace("-", "_");
            }
            else if (!string.IsNullOrEmpty(comp.Name))
            {
                baseName = comp.Name.ToLower().Replace(" ", "_").Replace("-", "_");
            }
            else
            {
                baseName = "component";
            }

            // Remove invalid characters for Python variable names
            baseName = System.Text.RegularExpressions.Regex.Replace(baseName, @"[^a-z0-9_]", "");
            if (string.IsNullOrEmpty(baseName) || char.IsDigit(baseName[0]))
            {
                baseName = "comp_" + baseName;
            }

            string shortUuid = GetShortUuid(comp.Guid, usedUuids);
            return $"{baseName}_{shortUuid}";
        }

        /// <summary>
        /// Performs topological sort on components based on their dependencies.
        /// </summary>
        private List<PseudocodeComponent> TopologicalSort(List<PseudocodeComponent> components)
        {
            var sorted = new List<PseudocodeComponent>();
            var visited = new HashSet<int>();
            var visiting = new HashSet<int>();
            var compById = components.ToDictionary(c => c.Id, c => c);

            void Visit(int compId)
            {
                if (visiting.Contains(compId))
                {
                    // Cycle detected - just add it anyway
                    return;
                }
                if (visited.Contains(compId)) return;

                visiting.Add(compId);

                if (compById.TryGetValue(compId, out var comp))
                {
                    // Visit all dependencies first
                    foreach (var input in comp.Inputs)
                    {
                        foreach (var connection in input.Connections)
                        {
                            Visit(connection[0]); // Visit source component
                        }
                    }

                    visited.Add(compId);
                    sorted.Add(comp);
                }

                visiting.Remove(compId);
            }

            // Visit all components
            foreach (var comp in components)
            {
                if (!visited.Contains(comp.Id))
                {
                    Visit(comp.Id);
                }
            }

            return sorted;
        }

        private void BroadcastCanvasInfo(string pseudocodeData, string correlationId)
        {
            LogDebug($"BroadcastCanvasInfo called. CorrelationId: {correlationId}");

            try
            {
                var responseDict = new Dictionary<string, object>
                {
                    ["action"] = "get_canvas_info_response",
                    ["correlationId"] = correlationId ?? Guid.NewGuid().ToString(),
                    ["status"] = "success",
                    ["data"] = pseudocodeData
                };

                string responseJson = JsonConvert.SerializeObject(responseDict);
                LogDebug($"Response created, length: {responseJson.Length}");

                if (!string.IsNullOrEmpty(correlationId))
                {
                    // Send directly to the requesting session
                    LiveCodingService.SendToSession(correlationId, responseJson);
                    LogDebug("Direct session send completed");
                }
                else
                {
                    LogDebug("No correlation ID - cannot send response");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Response creation failed: {ex.Message}");
            }
        }

        // ---------------------- Create Slider ----------------------

        private void CreateSlider(GH_Document doc, IDictionary<string, object> payload, string correlationId)
        {
            try
            {
                var x = F(payload.TryGetValue("x", out var xv) ? xv : null, 150f);
                var y = F(payload.TryGetValue("y", out var yv) ? yv : null, 150f);
                var name = S(payload.TryGetValue("nickname", out var nv) ? nv : null, "From VSCode");

                var slider = new GH_NumberSlider();
                slider.CreateAttributes();
                slider.Attributes.Pivot = new PointF(x, y);
                slider.NickName = name;

                doc.AddObject(slider, true);

                SendSuccessResponse("create_slider", correlationId, $"Slider '{name}' created successfully");
                LogDebug($"Slider created successfully: {name} at ({x}, {y})");
            }
            catch (Exception ex)
            {
                var error = $"Failed to create slider: {ex.Message}";
                LogDebug(error);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error);
                SendErrorResponse("create_slider", correlationId, error);
            }
        }

        // ---------------------- Create Python Script ----------------------
        // Rhino 7 path: RhinoCodePluginGH.Components.Python3Component.Create(...)
        // Overloads differ; we disambiguate by parameter types.
        // Legacy fallback: GhPython.Component.ZuiPythonComponent with property "Code"/etc.

        private void CreatePythonScript(GH_Document doc, IDictionary<string, object> payload, string correlationId)
        {
            try
            {
                var x = F(payload.TryGetValue("x", out var xv) ? xv : null, 260f);
                var y = F(payload.TryGetValue("y", out var yv) ? yv : null, 160f);
                var code = S(payload.TryGetValue("code", out var cv) ? cv : null,
                    "import datetime as _dt\nA = 'Py ready @ ' + _dt.datetime.now().strftime('%H:%M:%S')");

                // Try Rhino 8 API first (RhinoCodePluginGH)
                var created = TryCreateRhino8Python(doc, x, y, code);
                if (created)
                {
                    SendSuccessResponse("create_python_script", correlationId, "Python script component created successfully using Rhino 8 API");
                    return;
                }

                // Fallback to legacy GhPython (Rhino 7 / legacy in Rhino 8)
                var ok = TryCreateLegacyGhPython(doc, x, y, code);
                if (ok)
                {
                    SendSuccessResponse("create_python_script", correlationId, "Python script component created successfully using legacy API");
                    return;
                }

                // Both methods failed
                var error = "Could not create a Python script component. Ensure RhinoCode or GHPython is available.";
                FallbackPanel(doc, x, y,
                    "Could not create a Python script component.\n" +
                    "Rhino 8: ensure RhinoCode plugin is loaded.\n" +
                    "Legacy: ensure GHPython is installed.");

                SendErrorResponse("create_python_script", correlationId, error);
            }
            catch (Exception ex)
            {
                var error = $"Failed to create Python script: {ex.Message}";
                LogDebug(error);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error);
                SendErrorResponse("create_python_script", correlationId, error);
            }
        }

        private bool TryCreateRhino8Python(GH_Document doc, float x, float y, string code)
        {
            var t = Type.GetType("RhinoCodePluginGH.Components.Python3Component, RhinoCodePluginGH");
            if (t == null) return false;

            IGH_DocumentObject obj = null;

            // Prefer overload: Create(string nickname, string source)
            var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                           .Where(m => m.Name == "Create")
                           .ToArray();

            var createStringString = methods.FirstOrDefault(m =>
            {
                var ps = m.GetParameters();
                return ps.Length == 2 &&
                       ps[0].ParameterType == typeof(string) &&
                       ps[1].ParameterType == typeof(string);
            });

            if (createStringString != null)
            {
                var o = createStringString.Invoke(null, new object[] { "LivePython", code });
                obj = o as IGH_DocumentObject;
            }
            else
            {
                // Overload: Create(string nickname, Bitmap icon, bool openEditor)
                var createStringBitmapBool = methods.FirstOrDefault(m =>
                {
                    var ps = m.GetParameters();
                    return ps.Length == 3 &&
                           ps[0].ParameterType == typeof(string) &&
                           ps[1].ParameterType == typeof(Bitmap) &&
                           ps[2].ParameterType == typeof(bool);
                });

                if (createStringBitmapBool != null)
                {
                    using (var bmp = new Bitmap(16, 16))
                    {
                        var o = createStringBitmapBool.Invoke(null, new object[] { "LivePython", bmp, false });
                        obj = o as IGH_DocumentObject;

                        // If Create overload did not accept source, set it via SetSource afterwards
                        if (obj != null)
                        {
                            var setSource = t.GetMethod("SetSource", BindingFlags.Public | BindingFlags.Instance);
                            if (setSource != null)
                            {
                                try { setSource.Invoke(obj, new object[] { code }); } catch { /* ignore */ }
                            }
                        }
                    }
                }
            }

            if (obj == null) return false;

            obj.CreateAttributes();
            obj.Attributes.Pivot = new PointF(x, y);
            doc.AddObject(obj, true);
            doc.ScheduleSolution(1, _ => obj.ExpireSolution(true));
            return true;
        }

        private bool TryCreateLegacyGhPython(GH_Document doc, float x, float y, string code)
        {
            // Old component: GhPython.Component.ZuiPythonComponent (IronPython)
            var t = Type.GetType("GhPython.Component.ZuiPythonComponent, GhPython");
            if (t == null) return false;

            var obj = Activator.CreateInstance(t) as IGH_DocumentObject;
            if (obj == null) return false;

            obj.CreateAttributes();
            obj.Attributes.Pivot = new PointF(x, y);

            // Set legacy code property if available
            TrySetScriptCode(obj, code, out _);

            doc.AddObject(obj, true);
            doc.ScheduleSolution(1, _ => obj.ExpireSolution(true));
            return true;
        }

        // ---------------------- Advanced Python Component Creation (Rhino 8.14+) ----------------------

        private void CreatePythonAdvanced(GH_Document doc, IDictionary<string, object> payload, string correlationId)
        {
            try
            {
                var x = F(payload.TryGetValue("x", out var xv) ? xv : null, 300f);
                var y = F(payload.TryGetValue("y", out var yv) ? yv : null, 200f);
                var code = S(payload.TryGetValue("code", out var cv) ? cv : null,
                    "# Advanced Python Component\nimport Rhino.Geometry as rg\n\n# Process inputs\nresult = []\nfor i, item in enumerate(input_data if 'input_data' in locals() else []):\n    result.append(f\"Item {i}: {item}\")\n\noutput = result");

                // Extract input/output specifications
                var inputs = payload.TryGetValue("inputs", out var inputsObj) ?
                    JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(JsonConvert.SerializeObject(inputsObj)) ??
                    new List<Dictionary<string, object>>() : new List<Dictionary<string, object>>();

                var outputs = payload.TryGetValue("outputs", out var outputsObj) ?
                    JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(JsonConvert.SerializeObject(outputsObj)) ??
                    new List<Dictionary<string, object>>() : new List<Dictionary<string, object>>();

                // Extract connection specifications
                var connections = payload.TryGetValue("connections", out var connectionsObj) ?
                    JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(JsonConvert.SerializeObject(connectionsObj)) ??
                    new List<Dictionary<string, object>>() : new List<Dictionary<string, object>>();

                LogDebug($"CreatePythonAdvanced: inputs={inputs.Count}, outputs={outputs.Count}, connections={connections.Count}");

                // Try the official Rhino 8.14+ API
                var pythonComponent = CreateAdvancedPython814(doc, x, y, code, inputs, outputs, connections);
                if (pythonComponent != null)
                {
                    var message = $"Advanced Python component created successfully with {inputs.Count} inputs, {outputs.Count} outputs, {connections.Count} connections";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, message);
                    SendSuccessResponse("create_python_advanced", correlationId, message);
                    return;
                }

                // API not available - send error response
                var error = "Advanced Python Creation Failed: Rhino 8.14+ API not available. Try create_python_xml endpoint instead.";
                FallbackPanel(doc, x, y,
                    "Advanced Python Creation Failed.\n" +
                    "Rhino 8.14+ API not available.\n" +
                    "Try create_python_xml endpoint instead.");

                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, error);
                SendErrorResponse("create_python_advanced", correlationId, error);
            }
            catch (Exception ex)
            {
                var error = $"Failed to create advanced Python component: {ex.Message}";
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error);
                LogDebug($"CreatePythonAdvanced exception: {ex}");
                SendErrorResponse("create_python_advanced", correlationId, error);
            }
        }

        private IGH_Component CreateAdvancedPython814(GH_Document doc, float x, float y, string code,
            List<Dictionary<string, object>> inputs, List<Dictionary<string, object>> outputs,
            List<Dictionary<string, object>> connections)
        {
            try
            {
                // Try to use the direct API approach from Ehsan's forum post
                LogDebug("Attempting to create Python3Component using direct API");

                // First check if the types are available
                var pythonType = Type.GetType("RhinoCodePluginGH.Components.Python3Component, RhinoCodePluginGH");
                var paramType = Type.GetType("RhinoCodePluginGH.Parameters.ScriptVariableParam, RhinoCodePluginGH");

                if (pythonType == null)
                {
                    LogDebug("RhinoCodePluginGH.Components.Python3Component type not found - API not available");
                    return null;
                }

                if (paramType == null)
                {
                    LogDebug("RhinoCodePluginGH.Parameters.ScriptVariableParam type not found - API not available");
                    return null;
                }

                // Try the Create method as shown in forum: Python3Component.Create("MyScript", code)
                var createMethod = pythonType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
                if (createMethod == null)
                {
                    LogDebug("Python3Component.Create static method not found");
                    return null;
                }

                // Create the component using the API
                object component = null;
                var createParams = createMethod.GetParameters();

                // Try different Create overloads
                if (createParams.Length == 2 &&
                    createParams[0].ParameterType == typeof(string) &&
                    createParams[1].ParameterType == typeof(string))
                {
                    // Create(string name, string source)
                    component = createMethod.Invoke(null, new object[] { "AdvancedPython", code });
                    LogDebug("Used Create(string, string) overload");
                }
                else if (createParams.Length == 1 && createParams[0].ParameterType == typeof(string))
                {
                    // Create(string name) - then set source separately
                    component = createMethod.Invoke(null, new object[] { "AdvancedPython" });
                    if (component != null)
                    {
                        var setSourceMethod = pythonType.GetMethod("SetSource");
                        setSourceMethod?.Invoke(component, new object[] { code });
                    }
                    LogDebug("Used Create(string) overload with separate SetSource");
                }
                else
                {
                    LogDebug($"No suitable Create method found. Available parameters: {createParams.Length}");
                    return null;
                }

                if (component == null)
                {
                    LogDebug("Failed to create Python3Component instance");
                    return null;
                }

                var ghComponent = component as IGH_Component;
                if (ghComponent == null)
                {
                    LogDebug("Created component is not IGH_Component");
                    return null;
                }

                LogDebug($"Successfully created Python3Component, adding {inputs.Count} inputs and {outputs.Count} outputs");

                // Add custom inputs using the ScriptVariableParam approach from forum
                foreach (var inputSpec in inputs)
                {
                    var name = S(inputSpec.TryGetValue("name", out var nameObj) ? nameObj : null, "input");
                    var nickname = S(inputSpec.TryGetValue("nickname", out var nickObj) ? nickObj : null, name);
                    var optional = inputSpec.TryGetValue("optional", out var optObj) && optObj is bool opt ? opt : true;
                    var access = S(inputSpec.TryGetValue("access", out var accessObj) ? accessObj : null, "item");

                    // Create ScriptVariableParam as shown in forum: new ScriptVariableParam("first")
                    var inputParam = Activator.CreateInstance(paramType, new object[] { name });
                    if (inputParam != null)
                    {
                        // Set properties as shown in forum
                        var prettyNameProp = paramType.GetProperty("PrettyName");
                        prettyNameProp?.SetValue(inputParam, nickname);

                        var optionalProp = paramType.GetProperty("Optional");
                        optionalProp?.SetValue(inputParam, optional);

                        var accessProp = paramType.GetProperty("Access");
                        if (accessProp != null)
                        {
                            GH_ParamAccess accessValue = GH_ParamAccess.item;
                            var accessLower = access.ToLower();
                            if (accessLower == "list")
                                accessValue = GH_ParamAccess.list;
                            else if (accessLower == "tree")
                                accessValue = GH_ParamAccess.tree;

                            accessProp.SetValue(inputParam, accessValue);
                        }

                        // CreateAttributes and register as shown in forum
                        var createAttribMethod = inputParam.GetType().GetMethod("CreateAttributes");
                        createAttribMethod?.Invoke(inputParam, null);

                        var registerInputMethod = ghComponent.Params.GetType().GetMethod("RegisterInputParam");
                        registerInputMethod?.Invoke(ghComponent.Params, new object[] { inputParam });

                        LogDebug($"Added input parameter: {name} ({nickname})");
                    }
                }

                // Add custom outputs
                foreach (var outputSpec in outputs)
                {
                    var name = S(outputSpec.TryGetValue("name", out var nameObj) ? nameObj : null, "output");
                    var nickname = S(outputSpec.TryGetValue("nickname", out var nickObj) ? nickObj : null, name);

                    var outputParam = Activator.CreateInstance(paramType, new object[] { name });
                    if (outputParam != null)
                    {
                        var prettyNameProp = paramType.GetProperty("PrettyName");
                        prettyNameProp?.SetValue(outputParam, nickname);

                        var createAttribMethod = outputParam.GetType().GetMethod("CreateAttributes");
                        createAttribMethod?.Invoke(outputParam, null);

                        var registerOutputMethod = ghComponent.Params.GetType().GetMethod("RegisterOutputParam");
                        registerOutputMethod?.Invoke(ghComponent.Params, new object[] { outputParam });

                        LogDebug($"Added output parameter: {name} ({nickname})");
                    }
                }

                // Call VariableParameterMaintenance as shown in forum
                var maintenanceMethod = pythonType.GetMethod("VariableParameterMaintenance");
                if (maintenanceMethod != null)
                {
                    maintenanceMethod.Invoke(component, null);
                    LogDebug("Called VariableParameterMaintenance");
                }
                else
                {
                    LogDebug("VariableParameterMaintenance method not found");
                }

                // Position and add to document
                ghComponent.CreateAttributes();
                ghComponent.Attributes.Pivot = new PointF(x, y);
                doc.AddObject(ghComponent, true);

                LogDebug("Component added to document successfully");

                // Handle connections after adding to document
                doc.ScheduleSolution(15, _ => {
                    try
                    {
                        MakeConnections(doc, ghComponent, connections);
                        ghComponent.ExpireSolution(true);
                        LogDebug("Connections and solution update completed");
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"Connection failed: {ex.Message}");
                    }
                });

                return ghComponent;
            }
            catch (Exception ex)
            {
                LogDebug($"CreateAdvancedPython814 failed: {ex.Message}");
                return null;
            }
        }

        // ---------------------- XML-based Python Component Creation ----------------------

        private void CreatePythonXml(GH_Document doc, IDictionary<string, object> payload, string correlationId)
        {
            try
            {
                var x = F(payload.TryGetValue("x", out var xv) ? xv : null, 400f);
                var y = F(payload.TryGetValue("y", out var yv) ? yv : null, 200f);
                var code = S(payload.TryGetValue("code", out var cv) ? cv : null,
                    "# XML-based Python Component\nimport Rhino.Geometry as rg\n\n# Process data\nresult = f\"XML Python executed at {System.DateTime.Now}\"");

                // Extract input/output specifications
                var inputs = payload.TryGetValue("inputs", out var inputsObj) ?
                    JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(JsonConvert.SerializeObject(inputsObj)) ??
                    new List<Dictionary<string, object>>() : new List<Dictionary<string, object>>();

                var outputs = payload.TryGetValue("outputs", out var outputsObj) ?
                    JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(JsonConvert.SerializeObject(outputsObj)) ??
                    new List<Dictionary<string, object>>() : new List<Dictionary<string, object>>();

                var connections = payload.TryGetValue("connections", out var connectionsObj) ?
                    JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(JsonConvert.SerializeObject(connectionsObj)) ??
                    new List<Dictionary<string, object>>() : new List<Dictionary<string, object>>();

                LogDebug($"CreatePythonXml: inputs={inputs.Count}, outputs={outputs.Count}, connections={connections.Count}");

                var pythonComponent = CreatePythonWithXml(doc, x, y, code, inputs, outputs, connections);
                if (pythonComponent != null)
                {
                    var message = $"XML-based Python component created successfully with {inputs.Count} inputs, {outputs.Count} outputs, {connections.Count} connections";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, message);
                    SendSuccessResponse("create_python_xml", correlationId, message);
                    return;
                }

                // Creation failed
                var error = "XML Python Creation Failed: Neither RhinoCode nor GhPython available. Install Python component support.";
                FallbackPanel(doc, x, y,
                    "XML Python Creation Failed.\n" +
                    "Neither RhinoCode nor GhPython available.\n" +
                    "Install Python component support.");

                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error);
                SendErrorResponse("create_python_xml", correlationId, error);
            }
            catch (Exception ex)
            {
                var error = $"Failed to create XML Python component: {ex.Message}";
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error);
                LogDebug($"CreatePythonXml exception: {ex}");
                SendErrorResponse("create_python_xml", correlationId, error);
            }
        }

        private IGH_Component CreatePythonWithXml(GH_Document doc, float x, float y, string code,
            List<Dictionary<string, object>> inputs, List<Dictionary<string, object>> outputs,
            List<Dictionary<string, object>> connections)
        {
            // Try RhinoCode first
            var pythonType = Type.GetType("RhinoCodePluginGH.Components.Python3Component, RhinoCodePluginGH");
            if (pythonType == null)
            {
                // Fallback to legacy GhPython
                pythonType = Type.GetType("GhPython.Component.ZuiPythonComponent, GhPython");
                if (pythonType == null)
                {
                    LogDebug("No Python component types available");
                    return null;
                }
            }

            // Create basic component instance
            var component = Activator.CreateInstance(pythonType) as IGH_Component;
            if (component == null)
            {
                LogDebug("Failed to create component instance");
                return null;
            }

            // Add custom parameters using reflection
            AddCustomParameters(component, inputs, outputs);

            // Position component
            component.CreateAttributes();
            component.Attributes.Pivot = new PointF(x, y);

            // Use XML serialization to set the script code
            if (!SetCodeViaXml(component, code))
            {
                LogDebug("Failed to set code via XML, trying legacy approach");
                TrySetScriptCode(component, code, out var debug);
                LogDebug($"Legacy code setting: {debug}");
            }

            // Add to document
            doc.AddObject(component, true);

            // Handle connections after adding to document
            doc.ScheduleSolution(15, _ => {
                try
                {
                    MakeConnections(doc, component, connections);
                    component.ExpireSolution(true);
                }
                catch (Exception ex)
                {
                    LogDebug($"XML connection failed: {ex.Message}");
                }
            });

            return component;
        }

        private void AddCustomParameters(IGH_Component component, List<Dictionary<string, object>> inputs, List<Dictionary<string, object>> outputs)
        {
            try
            {
                // Try to add parameters using reflection
                var paramsProperty = component.GetType().GetProperty("Params");
                var paramServer = paramsProperty?.GetValue(component);
                if (paramServer == null)
                {
                    LogDebug("Could not access component parameter server");
                    return;
                }

                // Add inputs
                foreach (var inputSpec in inputs)
                {
                    var name = S(inputSpec.TryGetValue("name", out var nameObj) ? nameObj : null, "input");
                    var nickname = S(inputSpec.TryGetValue("nickname", out var nickObj) ? nickObj : null, name);

                    // Create a generic parameter - try different types
                    IGH_Param inputParam = TryCreateParameter("ScriptVariableParam") ??
                                          TryCreateParameter("Param_GenericObject") ??
                                          TryCreateParameter("Param_String");

                    if (inputParam != null)
                    {
                        inputParam.Name = name;
                        inputParam.NickName = nickname;
                        inputParam.CreateAttributes();

                        // Use reflection to call RegisterInputParam
                        var registerMethod = paramServer.GetType().GetMethod("RegisterInputParam");
                        registerMethod?.Invoke(paramServer, new object[] { inputParam });
                    }
                }

                // Add outputs
                foreach (var outputSpec in outputs)
                {
                    var name = S(outputSpec.TryGetValue("name", out var nameObj) ? nameObj : null, "output");
                    var nickname = S(outputSpec.TryGetValue("nickname", out var nickObj) ? nickObj : null, name);

                    IGH_Param outputParam = TryCreateParameter("ScriptVariableParam") ??
                                           TryCreateParameter("Param_GenericObject") ??
                                           TryCreateParameter("Param_String");

                    if (outputParam != null)
                    {
                        outputParam.Name = name;
                        outputParam.NickName = nickname;
                        outputParam.CreateAttributes();

                        // Use reflection to call RegisterOutputParam
                        var registerMethod = paramServer.GetType().GetMethod("RegisterOutputParam");
                        registerMethod?.Invoke(paramServer, new object[] { outputParam });
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to add custom parameters: {ex.Message}");
            }
        }

        private IGH_Param TryCreateParameter(string typeName)
        {
            try
            {
                // Try RhinoCode first
                var paramType = Type.GetType($"RhinoCodePluginGH.Parameters.{typeName}, RhinoCodePluginGH") ??
                               Type.GetType($"Grasshopper.Kernel.Parameters.{typeName}, Grasshopper") ??
                               Type.GetType($"Grasshopper.Kernel.Parameters.{typeName}");

                if (paramType != null)
                {
                    return Activator.CreateInstance(paramType) as IGH_Param;
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to create parameter {typeName}: {ex.Message}");
            }
            return null;
        }

        private bool SetCodeViaXml(IGH_Component component, string code)
        {
            try
            {
                // Serialize component to XML
                var archive = new GH_Archive();
                archive.AppendObject(component, "Component");
                string xmlString = archive.Serialize_Xml();

                // Parse and modify XML
                var doc = XDocument.Parse(xmlString);

                // Find script elements - try multiple paths
                var scriptElement = doc.Descendants("Script").FirstOrDefault() ??
                                   doc.Descendants("Source").FirstOrDefault() ??
                                   doc.Descendants("Code").FirstOrDefault();

                if (scriptElement != null)
                {
                    var textElement = scriptElement.Element("Text") ?? scriptElement;
                    if (textElement != null)
                    {
                        // Encode code as base64
                        byte[] codeBytes = Encoding.UTF8.GetBytes(code);
                        string base64Code = Convert.ToBase64String(codeBytes);
                        textElement.Value = base64Code;

                        // Deserialize back
                        var newArchive = new GH_Archive();
                        newArchive.Deserialize_Xml(doc.ToString());

                        LogDebug("XML code setting successful");
                        return true;
                    }
                }

                LogDebug("Could not find script element in XML");
                return false;
            }
            catch (Exception ex)
            {
                LogDebug($"XML serialization failed: {ex.Message}");
                return false;
            }
        }

        private void MakeConnections(GH_Document doc, IGH_Component targetComponent, List<Dictionary<string, object>> connections)
        {
            foreach (var connection in connections)
            {
                try
                {
                    var sourceId = S(connection.TryGetValue("sourceId", out var srcIdObj) ? srcIdObj : null, "");
                    var sourceOutput = connection.TryGetValue("sourceOutput", out var srcOutObj) ?
                        Convert.ToInt32(srcOutObj) : 0;
                    var targetInput = connection.TryGetValue("targetInput", out var tgtInObj) ?
                        Convert.ToInt32(tgtInObj) : 0;

                    // Find source component
                    IGH_Component sourceComponent = null;
                    if (Guid.TryParse(sourceId, out var sourceGuid))
                    {
                        sourceComponent = doc.FindObject(sourceGuid, true) as IGH_Component;
                    }
                    else
                    {
                        // Find by nickname
                        sourceComponent = doc.Objects.OfType<IGH_Component>()
                            .FirstOrDefault(c => c.NickName == sourceId);
                    }

                    if (sourceComponent != null &&
                        sourceOutput < sourceComponent.Params.Output.Count &&
                        targetInput < targetComponent.Params.Input.Count)
                    {
                        var sourceParam = sourceComponent.Params.Output[sourceOutput];
                        var targetParam = targetComponent.Params.Input[targetInput];

                        targetParam.AddSource(sourceParam);
                        LogDebug($"Connected {sourceComponent.NickName}[{sourceOutput}] to {targetComponent.NickName}[{targetInput}]");
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"Individual connection failed: {ex.Message}");
                }
            }
        }

        // ---------------------- Update existing component by GUID ----------------------

        private void UpdateExistingScript(GH_Document doc, IDictionary<string, object> payload, string correlationId)
        {
            try
            {
                var idStr = S(payload.TryGetValue("componentId", out var idv) ? idv : null, "");
                var code = S(payload.TryGetValue("code", out var cv) ? cv : null, "");

                if (!Guid.TryParse(idStr, out var id))
                {
                    var error = $"Bad componentId '{idStr}'";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error);
                    SendErrorResponse("update_script", correlationId, error);
                    return;
                }

                var obj = doc.FindObject(id, true);
                if (obj == null)
                {
                    var error = $"Object {id} not found in document.";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, error);
                    SendErrorResponse("update_script", correlationId, error);
                    return;
                }

                // Rhino 8 API: prefer SetSource if this is a RhinoCode script component
                if (TrySetRhino8Source(obj, code))
                {
                    obj.ExpireSolution(true);
                    var message = $"Updated code on {obj.NickName} (Rhino 8 API)";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, message);
                    SendSuccessResponse("update_script", correlationId, message);
                    return;
                }

                // Legacy fallback
                if (TrySetScriptCode(obj, code, out var dbg))
                {
                    obj.ExpireSolution(true);
                    var message = $"Updated code on {obj.NickName} (legacy API)";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, message);
                    SendSuccessResponse("update_script", correlationId, message);
                }
                else
                {
                    var error = $"Could not set code on {obj.GetType().FullName}. {dbg}";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, error);
                    SendErrorResponse("update_script", correlationId, error);
                }
            }
            catch (Exception ex)
            {
                var error = $"Failed to update script: {ex.Message}";
                LogDebug(error);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error);
                SendErrorResponse("update_script", correlationId, error);
            }
        }

        private bool TrySetRhino8Source(IGH_DocumentObject obj, string code)
        {
            var t = obj.GetType();
            if (t.Namespace == null || !t.Namespace.StartsWith("RhinoCodePluginGH")) return false;

            var mi = t.GetMethod("SetSource", BindingFlags.Public | BindingFlags.Instance);
            if (mi == null) return false;

            try
            {
                var okObj = mi.Invoke(obj, new object[] { code });
                var ok = okObj is bool b ? b : true; // some builds may return void
                return ok;
            }
            catch
            {
                return false;
            }
        }

        // ---------------------- Reflection helper for legacy GhPython ----------------------

        private static bool TrySetScriptCode(object target, string code, out string debug)
        {
            debug = $"Target: {target.GetType().FullName}";
            var t = target.GetType();

            var propNames = new[] { "ScriptSource", "SourceCode", "Code", "Text", "Source" };
            foreach (var name in propNames)
            {
                var p = t.GetProperty(name, BF);
                if (p != null && p.CanWrite && p.PropertyType == typeof(string))
                {
                    p.SetValue(target, code);
                    debug += $"\nSet property {name}";
                    return true;
                }
            }

            var fieldNames = new[] { "m_py_code", "m_script", "_script", "_code", "m_source" };
            foreach (var name in fieldNames)
            {
                var f = t.GetField(name, BF);
                if (f != null && f.FieldType == typeof(string))
                {
                    f.SetValue(target, code);
                    debug += $"\nSet field {name}";
                    return true;
                }
            }

            foreach (var owner in new[] { "Script", "Editor" })
            {
                object nested = null;
                var p = t.GetProperty(owner, BF);
                if (p != null) nested = p.GetValue(target);
                var f = t.GetField(owner, BF);
                if (nested == null && f != null) nested = f.GetValue(target);
                if (nested == null) continue;

                var nt = nested.GetType();
                foreach (var name in propNames)
                {
                    var np = nt.GetProperty(name, BF);
                    if (np != null && np.CanWrite && np.PropertyType == typeof(string))
                    {
                        np.SetValue(nested, code);
                        debug += $"\nSet nested {owner}.{name}";
                        return true;
                    }
                }
                foreach (var name in fieldNames)
                {
                    var nf = nt.GetField(name, BF);
                    if (nf != null && nf.FieldType == typeof(string))
                    {
                        nf.SetValue(nested, code);
                        debug += $"\nSet nested {owner}.{name}";
                        return true;
                    }
                }
            }

            debug += "\nNo writable legacy code/script property or field found.";
            return false;
        }

        private static void FallbackPanel(GH_Document doc, float x, float y, string message)
        {
            var panel = new GH_Panel();
            panel.CreateAttributes();
            panel.Attributes.Pivot = new PointF(x, y);
            panel.NickName = "Info";
            panel.UserText = message;
            doc.AddObject(panel, true);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            try { _pump?.Stop(); _pump?.Dispose(); } catch { }
            if (_server != null && _server.IsListening) { try { _server.Stop(); } catch { } _server = null; }
            base.RemovedFromDocument(document);
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("4A5F8E6B-6F2E-4F92-A3B5-6B1C7C0D5B42");
    }

    /// <summary>
    /// Component that automatically creates a Python script component on the canvas
    /// </summary>
    public class CreatePythonScriptComponent : GH_Component
    {
        private bool _hasCreated = false;
        private bool _lastTriggerState = false;

        public CreatePythonScriptComponent()
            : base("Create Python Script", "CreatePy",
                   "Automatically creates a Python script component at 20,20 with custom inputs and outputs",
                   "Params", "Util")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Trigger", "T", "Trigger to create Python component", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "S", "Creation status", GH_ParamAccess.item);
            pManager.AddTextParameter("Component ID", "ID", "GUID of created component", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool trigger = false;
            if (!DA.GetData(0, ref trigger))
            {
                DA.SetData(0, "Ready - set Trigger to True to create Python component");
                DA.SetData(1, "");
                return;
            }

            // Only create when trigger changes from false to true (rising edge)
            bool shouldCreate = trigger && !_lastTriggerState && !_hasCreated;
            _lastTriggerState = trigger;

            if (!shouldCreate)
            {
                if (_hasCreated)
                {
                    DA.SetData(0, "Python component already created. Reset component to create another.");
                    DA.SetData(1, "");
                }
                else
                {
                    DA.SetData(0, "Ready - set Trigger to True to create Python component");
                    DA.SetData(1, "");
                }
                return;
            }

            try
            {
                var doc = OnPingDocument();
                if (doc == null)
                {
                    DA.SetData(0, "Error: No active document");
                    DA.SetData(1, "");
                    return;
                }

                // Create Python component using RhinoCodePluginGH API
                var success = CreatePythonComponentAdvanced(doc, out string componentId, out string statusMessage);

                if (success)
                {
                    _hasCreated = true;
                }

                DA.SetData(0, statusMessage);
                DA.SetData(1, componentId);
            }
            catch (Exception ex)
            {
                DA.SetData(0, $"Error: {ex.Message}");
                DA.SetData(1, "");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }
        }

        private bool CreatePythonComponentAdvanced(GH_Document doc, out string componentId, out string status)
        {
            componentId = "";
            status = "";

            try
            {
                // Reference RhinoCodePluginGH.rhp and get required types
                var pythonComponentType = Type.GetType("RhinoCodePluginGH.Components.Python3Component, RhinoCodePluginGH");
                var scriptVariableParamType = Type.GetType("RhinoCodePluginGH.Parameters.ScriptVariableParam, RhinoCodePluginGH");

                if (pythonComponentType == null)
                {
                    status = "Error: RhinoCodePluginGH.Components.Python3Component not found. Ensure RhinoCode plugin is loaded.";
                    return false;
                }

                if (scriptVariableParamType == null)
                {
                    status = "Error: RhinoCodePluginGH.Parameters.ScriptVariableParam not found. Ensure RhinoCode plugin is loaded.";
                    return false;
                }

                // Create Python component with custom code
                string customCode = @"
# Custom Python Script Component
import Rhino.Geometry as rg
import math

# Process first input (numbers)
if first:
    result_first = [x * 2 for x in first]
else:
    result_first = []

# Process second input (points)
if second:
    result_second = [rg.Point3d(pt.X + 10, pt.Y + 10, pt.Z) for pt in second]
else:
    result_second = []

# Generate output data
output = f'Processed {len(result_first)} numbers and {len(result_second)} points'

# Set outputs
first_output = result_first
second_output = result_second
";

                // Get all Create methods and find the right overload
                var createMethods = pythonComponentType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.Name == "Create")
                    .ToArray();

                object component = null;

                // Try the (string nickname, string source) overload first
                var stringStringMethod = createMethods.FirstOrDefault(m =>
                {
                    var ps = m.GetParameters();
                    return ps.Length == 2 &&
                           ps[0].ParameterType == typeof(string) &&
                           ps[1].ParameterType == typeof(string);
                });

                if (stringStringMethod != null)
                {
                    component = stringStringMethod.Invoke(null, new object[] { "MyScript", customCode });
                }
                else
                {
                    // Try (string nickname) overload and set source separately
                    var singleStringMethod = createMethods.FirstOrDefault(m =>
                    {
                        var ps = m.GetParameters();
                        return ps.Length == 1 && ps[0].ParameterType == typeof(string);
                    });

                    if (singleStringMethod != null)
                    {
                        component = singleStringMethod.Invoke(null, new object[] { "MyScript" });

                        // Set source code
                        var setSourceMethod = pythonComponentType.GetMethod("SetSource");
                        setSourceMethod?.Invoke(component, new object[] { customCode });
                    }
                    else
                    {
                        // Try (string nickname, Bitmap icon, bool openEditor) overload
                        var stringBitmapBoolMethod = createMethods.FirstOrDefault(m =>
                        {
                            var ps = m.GetParameters();
                            return ps.Length == 3 &&
                                   ps[0].ParameterType == typeof(string) &&
                                   ps[1].ParameterType == typeof(Bitmap) &&
                                   ps[2].ParameterType == typeof(bool);
                        });

                        if (stringBitmapBoolMethod != null)
                        {
                            using (var bmp = new Bitmap(16, 16))
                            {
                                component = stringBitmapBoolMethod.Invoke(null, new object[] { "MyScript", bmp, false });

                                // Set source code after creation
                                if (component != null)
                                {
                                    var setSourceMethod = pythonComponentType.GetMethod("SetSource");
                                    setSourceMethod?.Invoke(component, new object[] { customCode });
                                }
                            }
                        }
                    }
                }

                if (component == null)
                {
                    status = "Error: Failed to create Python3Component instance";
                    return false;
                }

                var ghComponent = component as IGH_Component;
                if (ghComponent == null)
                {
                    status = "Error: Created component is not IGH_Component";
                    return false;
                }

                // Add custom input parameters using ScriptVariableParam

                // First input: "first" - numbers
                var firstParam = Activator.CreateInstance(scriptVariableParamType, new object[] { "first" });
                if (firstParam != null)
                {
                    // Set properties as shown in provided syntax
                    var prettyNameProp = scriptVariableParamType.GetProperty("PrettyName");
                    prettyNameProp?.SetValue(firstParam, "First Input");

                    var toolTipProp = scriptVariableParamType.GetProperty("ToolTip");
                    toolTipProp?.SetValue(firstParam, "This is the first input");

                    var optionalProp = scriptVariableParamType.GetProperty("Optional");
                    optionalProp?.SetValue(firstParam, true);

                    var accessProp = scriptVariableParamType.GetProperty("Access");
                    accessProp?.SetValue(firstParam, GH_ParamAccess.list);

                    var allowTreeAccessProp = scriptVariableParamType.GetProperty("AllowTreeAccess");
                    allowTreeAccessProp?.SetValue(firstParam, true);

                    // Set type hints - TypeHints.Select(typeof(double))
                    var typeHintsProp = scriptVariableParamType.GetProperty("TypeHints");
                    var typeHints = typeHintsProp?.GetValue(firstParam);
                    if (typeHints != null)
                    {
                        var selectMethod = typeHints.GetType().GetMethod("Select", new Type[] { typeof(Type) });
                        selectMethod?.Invoke(typeHints, new object[] { typeof(double) });
                    }

                    // CreateAttributes and register
                    var createAttribMethod = firstParam.GetType().GetMethod("CreateAttributes");
                    createAttribMethod?.Invoke(firstParam, null);

                    var registerInputMethod = ghComponent.Params.GetType().GetMethod("RegisterInputParam", new Type[] { typeof(IGH_Param) });
                    registerInputMethod?.Invoke(ghComponent.Params, new object[] { firstParam });
                }

                // Second input: "second" - points
                var secondParam = Activator.CreateInstance(scriptVariableParamType, new object[] { "second" });
                if (secondParam != null)
                {
                    var prettyNameProp = scriptVariableParamType.GetProperty("PrettyName");
                    prettyNameProp?.SetValue(secondParam, "Second Input");

                    var toolTipProp = scriptVariableParamType.GetProperty("ToolTip");
                    toolTipProp?.SetValue(secondParam, "This is the second input");

                    var optionalProp = scriptVariableParamType.GetProperty("Optional");
                    optionalProp?.SetValue(secondParam, true);

                    var allowTreeAccessProp = scriptVariableParamType.GetProperty("AllowTreeAccess");
                    allowTreeAccessProp?.SetValue(secondParam, true);

                    // Set type hints - TypeHints.Select("Point3dList")
                    var typeHintsProp = scriptVariableParamType.GetProperty("TypeHints");
                    var typeHints = typeHintsProp?.GetValue(secondParam);
                    if (typeHints != null)
                    {
                        var selectMethod = typeHints.GetType().GetMethod("Select", new Type[] { typeof(string) });
                        selectMethod?.Invoke(typeHints, new object[] { "Point3dList" });
                    }

                    var createAttribMethod = secondParam.GetType().GetMethod("CreateAttributes");
                    createAttribMethod?.Invoke(secondParam, null);

                    var registerInputMethod = ghComponent.Params.GetType().GetMethod("RegisterInputParam", new Type[] { typeof(IGH_Param) });
                    registerInputMethod?.Invoke(ghComponent.Params, new object[] { secondParam });
                }

                // Add custom output parameters

                // Main output: "output"
                var outputParam = Activator.CreateInstance(scriptVariableParamType, new object[] { "output" });
                if (outputParam != null)
                {
                    var hiddenProp = scriptVariableParamType.GetProperty("Hidden");
                    hiddenProp?.SetValue(outputParam, true);

                    var createAttribMethod = outputParam.GetType().GetMethod("CreateAttributes");
                    createAttribMethod?.Invoke(outputParam, null);

                    var registerOutputMethod = ghComponent.Params.GetType().GetMethod("RegisterOutputParam", new Type[] { typeof(IGH_Param) });
                    registerOutputMethod?.Invoke(ghComponent.Params, new object[] { outputParam });
                }

                // Additional outputs for processed data
                var firstOutputParam = Activator.CreateInstance(scriptVariableParamType, new object[] { "first_output" });
                if (firstOutputParam != null)
                {
                    var prettyNameProp = scriptVariableParamType.GetProperty("PrettyName");
                    prettyNameProp?.SetValue(firstOutputParam, "First Output");

                    var createAttribMethod = firstOutputParam.GetType().GetMethod("CreateAttributes");
                    createAttribMethod?.Invoke(firstOutputParam, null);

                    var registerOutputMethod = ghComponent.Params.GetType().GetMethod("RegisterOutputParam", new Type[] { typeof(IGH_Param) });
                    registerOutputMethod?.Invoke(ghComponent.Params, new object[] { firstOutputParam });
                }

                var secondOutputParam = Activator.CreateInstance(scriptVariableParamType, new object[] { "second_output" });
                if (secondOutputParam != null)
                {
                    var prettyNameProp = scriptVariableParamType.GetProperty("PrettyName");
                    prettyNameProp?.SetValue(secondOutputParam, "Second Output");

                    var createAttribMethod = secondOutputParam.GetType().GetMethod("CreateAttributes");
                    createAttribMethod?.Invoke(secondOutputParam, null);

                    var registerOutputMethod = ghComponent.Params.GetType().GetMethod("RegisterOutputParam", new Type[] { typeof(IGH_Param) });
                    registerOutputMethod?.Invoke(ghComponent.Params, new object[] { secondOutputParam });
                }

                // Call VariableParameterMaintenance as shown in syntax
                var maintenanceMethod = pythonComponentType.GetMethod("VariableParameterMaintenance");
                maintenanceMethod?.Invoke(component, null);

                // Set special parameters as shown in syntax
                var usingStandardOutputProp = pythonComponentType.GetProperty("UsingStandardOutputParam");
                usingStandardOutputProp?.SetValue(component, true);

                var graftStandardOutputProp = pythonComponentType.GetProperty("GraftStandardOutputLines");
                graftStandardOutputProp?.SetValue(component, true);

                var marshGuidsProp = pythonComponentType.GetProperty("MarshGuids");
                marshGuidsProp?.SetValue(component, false);

                // Position component at 20,20 and add to document
                ghComponent.CreateAttributes();
                ghComponent.Attributes.Pivot = new PointF(20f, 20f);

                // Add to document safely
                try
                {
                    doc.AddObject(ghComponent, true);

                    // Schedule a solution to update the component
                    doc.ScheduleSolution(10, _ => {
                        try
                        {
                            ghComponent.ExpireSolution(true);
                        }
                        catch { /* ignore solution errors */ }
                    });
                }
                catch (Exception ex)
                {
                    status = $"Error adding component to document: {ex.Message}";
                    return false;
                }

                componentId = ghComponent.InstanceGuid.ToString();
                status = $"Python component created successfully at (20,20) with ID: {componentId}";

                return true;
            }
            catch (Exception ex)
            {
                status = $"Error creating Python component: {ex.Message}";
                return false;
            }
        }

        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);
            Menu_AppendItem(menu, "Reset Component", ResetComponent, true, false);
        }

        private void ResetComponent(object sender, EventArgs e)
        {
            _hasCreated = false;
            _lastTriggerState = false;
            ExpireSolution(true);
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("7B3E9F2A-8C5D-4A1F-9E6B-2C4F8A7E3D5B");
    }

    // ---------------------- WS plumbing ----------------------

    public class LiveCodingService : WebSocketBehavior
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<CommandMessage> _queue;
        private readonly Action<string> _logger;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, LiveCodingService> _sessions =
            new System.Collections.Concurrent.ConcurrentDictionary<string, LiveCodingService>();

        public LiveCodingService(System.Collections.Concurrent.ConcurrentQueue<CommandMessage> q, Action<string> logger = null)
        {
            _queue = q;
            _logger = logger ?? ((msg) => { /* no-op if no logger provided */ }); // no-op if no logger provided
        }

        protected override void OnOpen()
        {
            base.OnOpen();
        }

        protected override void OnClose(CloseEventArgs e)
        {
            base.OnClose(e);
        }

        protected override void OnError(ErrorEventArgs e)
        {
            _logger($"WebSocket error: {e.Message}");
            base.OnError(e);
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            try
            {
                var data = e.Data ?? "";
                _logger($"Received raw message: {data}");

                // Proper JSON deserialization instead of fragile string parsing
                CommandMessage msg;
                try
                {
                    msg = JsonConvert.DeserializeObject<CommandMessage>(data);
                    if (msg == null)
                    {
                        _logger("Failed to deserialize message - result was null");
                        return;
                    }
                }
                catch (JsonException ex)
                {
                    _logger($"JSON parsing failed: {ex.Message}");
                    var errorResponse = JsonConvert.SerializeObject(new { action = "error", message = "Invalid JSON format" });
                    Send(errorResponse);
                    return;
                }

                // Validate required fields
                if (string.IsNullOrEmpty(msg.Action))
                {
                    _logger("Action field is missing or empty");
                    var errorResponse = JsonConvert.SerializeObject(new { action = "error", message = "Action field is required" });
                    Send(errorResponse);
                    return;
                }

                if (string.IsNullOrEmpty(msg.CorrelationId))
                {
                    _logger("CorrelationId field is missing, generating default");
                    msg.CorrelationId = Guid.NewGuid().ToString();
                }

                _logger($"Parsed message - Action: {msg.Action}, CorrelationId: {msg.CorrelationId}");

                // Register session for responses
                _sessions[msg.CorrelationId] = this;

                // Queue the message for processing
                _queue.Enqueue(msg);

                // Send acknowledgment response
                var response = JsonConvert.SerializeObject(new
                {
                    action = msg.Action + "_response",
                    correlationId = msg.CorrelationId,
                    status = "queued"
                });
                Send(response);
                _logger($"Sent queued response for {msg.Action}");

            }
            catch (Exception ex)
            {
                _logger($"OnMessage exception: {ex.Message}");
                var errorResponse = JsonConvert.SerializeObject(new { action = "error", message = ex.Message });
                Send(errorResponse);
            }
        }

        public static void SendToSession(string correlationId, string message)
        {
            if (_sessions.TryGetValue(correlationId, out var session))
            {
                try
                {
                    // Check message size and truncate if too large
                    const int MAX_MESSAGE_SIZE = 100000; // 100KB limit
                    string messageToSend = message;

                    if (message.Length > MAX_MESSAGE_SIZE)
                    {
                        // Create a truncated response
                        var errorResponse = JsonConvert.SerializeObject(new
                        {
                            action = "get_canvas_info_response",
                            correlationId = correlationId,
                            status = "error",
                            message = $"Response too large ({message.Length} chars). Canvas has too many components to transmit via WebSocket."
                        });
                        messageToSend = errorResponse;
                        session._logger?.Invoke($"Message truncated due to size: {message.Length} chars");
                    }

                    session._logger?.Invoke($"Attempting to send message of size: {messageToSend.Length}");
                    session.Send(messageToSend);
                    session._logger?.Invoke($"Message sent successfully");
                    _sessions.TryRemove(correlationId, out _); // Clean up
                }
                catch (Exception ex)
                {
                    session._logger?.Invoke($"SendToSession failed: {ex.GetType().Name} - {ex.Message}");

                    // Try to send error response
                    try
                    {
                        var errorResponse = JsonConvert.SerializeObject(new
                        {
                            action = "get_canvas_info_response",
                            correlationId = correlationId,
                            status = "error",
                            message = $"WebSocket send failed: {ex.Message}"
                        });
                        session.Send(errorResponse);
                        session._logger?.Invoke($"Error response sent successfully");
                    }
                    catch (Exception ex2)
                    {
                        session._logger?.Invoke($"Failed to send error response: {ex2.Message}");
                    }

                    _sessions.TryRemove(correlationId, out _);
                }
            }
            else
            {
                // No session found - log this
                var anySession = _sessions.Values.FirstOrDefault();
                anySession?._logger?.Invoke($"SendToSession: No session found for correlationId: {correlationId}");
            }
        }
    }

    public class CommandMessage
    {
        [JsonProperty("action")]
        public string Action { get; set; }

        [JsonProperty("correlationId")]
        public string CorrelationId { get; set; }

        [JsonProperty("payload")]
        public Dictionary<string, object> Payload { get; set; }
    }

    // ---------------------- Pseudocode Data Models ----------------------

    public class PseudocodeComponent
    {
        public int Id { get; set; }
        public Guid Guid { get; set; }
        public string Name { get; set; }
        public string NickName { get; set; }
        public string Description { get; set; }
        public bool IsComponent { get; set; }
        public string VariableName { get; set; }
        public List<PseudocodeInput> Inputs { get; set; } = new List<PseudocodeInput>();
        public List<PseudocodeOutput> Outputs { get; set; } = new List<PseudocodeOutput>();
    }

    public class PseudocodeInput
    {
        public string Name { get; set; }
        public string TypeName { get; set; }
        public List<int[]> Connections { get; set; } = new List<int[]>();
    }

    public class PseudocodeOutput
    {
        public string Name { get; set; }
        public string TypeName { get; set; }
        public string DataPreview { get; set; }
        public bool HasRecipients { get; set; }
    }
}
