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
    ///   - create_python_component            { x?, y?, code?, inputs[], outputs[], connections[] }
    ///   - update_script                      { componentId, code }
    ///   - get_canvas_info                    { includeDataPreviews?, maxPreviewLength? } (returns JSON of entire canvas definition)
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
            if (DebugLog.Count > 2000)
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

                    case "create_python_component":
                        CreatePythonComponent(doc, payload, cmd.CorrelationId);
                        break;

                    case "update_script":
                        UpdateExistingScript(doc, payload, cmd.CorrelationId);
                        break;

                    case "get_canvas_info":
                        GetCanvasInfo(doc, payload, cmd.CorrelationId);
                        break;

                    case "get_selection":
                        GetSelection(doc, cmd.CorrelationId);
                        break;

                    case "manage_wires":
                        ManageWires(doc, payload, cmd.CorrelationId);
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

        private void SendSuccessResponseWithData(string action, string correlationId, string message = null, Dictionary<string, object> data = null)
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

                if (data != null)
                {
                    responseDict["data"] = data;
                }

                string responseJson = JsonConvert.SerializeObject(responseDict);
                LogDebug($"Sending success response with data for {action}");

                if (!string.IsNullOrEmpty(correlationId))
                {
                    LiveCodingService.SendToSession(correlationId, responseJson);
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to send success response with data: {ex.Message}");
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

        private const int FULL_DATA_CHAR_LIMIT = 10000;
        private const int DATA_PREVIEW_LIMIT = 100;

        private void GetCanvasInfo(GH_Document doc, Dictionary<string, object> payload, string correlationId = null)
        {
            try
            {
                // Parse optional parameters for data preview control
                bool includeDataPreviews = payload != null && payload.ContainsKey("includeDataPreviews") && payload["includeDataPreviews"] is bool b && b;
                int maxPreviewLength = payload != null && payload.ContainsKey("maxPreviewLength") && payload["maxPreviewLength"] is int len ? len : 20;

                LogDebug($"GetCanvasInfo started. Doc null? {doc == null}, includeDataPreviews: {includeDataPreviews}, maxPreviewLength: {maxPreviewLength}");
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

                        // Get component position
                        if (ghComponent.Attributes != null)
                        {
                            comp.X = ghComponent.Attributes.Pivot.X;
                            comp.Y = ghComponent.Attributes.Pivot.Y;
                        }

                        // Determine component type from primary output or component name
                        var mainOutput = ghComponent.Params.Output.FirstOrDefault();
                        comp.ComponentType = mainOutput?.TypeName ?? "Object";

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
                            comp.Inputs.Add(new PseudocodeInput {
                                Name = inputParam.Name,
                                TypeName = inputParam.TypeName,
                                Connections = connections,
                                ParameterUuid = inputParam.InstanceGuid,
                                HasConnections = inputParam.Sources.Count > 0,
                                IsOptional = inputParam.Optional
                            });
                        }

                        // Parse outputs
                        comp.Outputs = ghComponent.Params.Output.Select(p => {
                            var data = p.VolatileData.AllData(true);
                            var dataPreview = data.Any() ? string.Join(", ", data.Select(GetCompactDataPreview)) : "null";

                            return new PseudocodeOutput {
                                Name = p.Name,
                                TypeName = p.TypeName,
                                DataPreview = dataPreview,
                                HasRecipients = p.Recipients.Count > 0,
                                ParameterUuid = p.InstanceGuid
                            };
                        }).ToList();
                    }
                    else if (obj is IGH_Param ghParam)
                    {
                        comp.Name = ghParam.Name;
                        comp.NickName = ghParam.NickName;
                        comp.Description = ghParam.Description;
                        comp.IsComponent = false;

                        // Get parameter position
                        if (ghParam.Attributes != null)
                        {
                            comp.X = ghParam.Attributes.Pivot.X;
                            comp.Y = ghParam.Attributes.Pivot.Y;
                        }

                        // Component type is the parameter type
                        comp.ComponentType = ghParam.TypeName;

                        var data = ghParam.VolatileData.AllData(true);
                        var dataPreview = data.Any() ? string.Join(", ", data.Select(GetCompactDataPreview)) : "null";

                        comp.Outputs.Add(new PseudocodeOutput {
                            Name = ghParam.Name,
                            TypeName = ghParam.TypeName,
                            DataPreview = dataPreview,
                            HasRecipients = ghParam.Recipients.Count > 0,
                            ParameterUuid = ghParam.InstanceGuid
                        });

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
                            comp.Inputs.Add(new PseudocodeInput {
                                Name = "Input",
                                TypeName = ghParam.TypeName,
                                Connections = connections,
                                ParameterUuid = ghParam.InstanceGuid,
                                HasConnections = ghParam.Sources.Count > 0,
                                IsOptional = false // Parameters typically aren't optional
                            });
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

                // Generate Enhanced Pipe-Delimited with Types pseudocode
                var output = new StringBuilder();

                // Header
                output.AppendLine("# === GRASSHOPPER DEFINITION PSEUDOCODE (Enhanced Pipe-Delimited with Types) ===");
                output.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                var sourceCount = sortedComponents.Count(c => !c.Inputs.Any());
                var sinkCount = sortedComponents.Count(c => !c.Outputs.Any(o => o.HasRecipients));
                var totalConnections = sortedComponents.Sum(c => c.Inputs.Sum(i => i.Connections.Count));

                output.AppendLine($"# Components: {sortedComponents.Count} | Connections: {totalConnections} | Sources: {sourceCount} | Sinks: {sinkCount}");
                output.AppendLine($"# Format: variable|x,y|comp_uuid: ComponentType = \"Component Name\" | [\"Input Name\"(InputType):param_uuid] | [\"Output Name\"(OutputType):param_uuid]");
                output.AppendLine();

                // Generate all components in the new pipe-delimited format
                foreach (var comp in sortedComponents)
                {
                    // Format: variable|x,y|comp_uuid: ComponentType = "Component Name" | ["Input Name"(InputType):param_uuid] | ["Output Name"(OutputType):param_uuid]

                    var position = $"{(int)comp.X},{(int)comp.Y}";
                    var compUuid = comp.Guid.ToString();
                    var componentType = comp.ComponentType ?? "Object";
                    var componentName = comp.Name ?? comp.NickName ?? "Component";

                    // Build input section
                    var inputParts = new List<string>();
                    foreach (var input in comp.Inputs)
                    {
                        var inputName = input.Name;
                        var inputType = input.TypeName ?? "Object";
                        var paramUuid = input.ParameterUuid.ToString();
                        inputParts.Add($"\"{inputName}\"({inputType}):{paramUuid}");
                    }
                    var inputSection = inputParts.Any() ? $"[{string.Join(", ", inputParts)}]" : "[]";

                    // Build output section
                    var outputParts = new List<string>();
                    foreach (var outputParam in comp.Outputs)
                    {
                        var outputType = outputParam.TypeName ?? "Object";
                        var paramUuid = outputParam.ParameterUuid.ToString();
                        var paramDef = $"\"{outputParam.Name}\"({outputType}):{paramUuid}";

                        // Optionally include data preview inline
                        if (includeDataPreviews && !string.IsNullOrEmpty(outputParam.DataPreview) && outputParam.DataPreview != "null")
                        {
                            var preview = outputParam.DataPreview;
                            // Truncate to maxPreviewLength
                            if (preview.Length > maxPreviewLength)
                            {
                                preview = preview.Substring(0, maxPreviewLength - 2) + "..";
                            }
                            // Escape double quotes for embedding in string
                            preview = preview.Replace("\"", "\\\"");
                            paramDef += $"=\"{preview}\"";
                        }

                        outputParts.Add(paramDef);
                    }
                    var outputSection = outputParts.Any() ? $"[{string.Join(", ", outputParts)}]" : "[]";

                    // Generate the complete line
                    var line = $"{comp.VariableName}|{position}|{compUuid}: {componentType} = \"{componentName}\" | {inputSection} | {outputSection}";

                    output.AppendLine(line);
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
        /// Returns full UUID with standard hyphenated format.
        /// </summary>
        private string GetFullUuid(Guid guid, HashSet<string> usedUuids)
        {
            string uuidStr = guid.ToString(); // Standard format with hyphens (36 characters)
            usedUuids.Add(uuidStr);
            return uuidStr;
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

            string fullUuid = GetFullUuid(comp.Guid, usedUuids);

            // Store full UUID for connection purposes
            comp.FullUuidString = comp.Guid.ToString();

            return $"{baseName}_{fullUuid}";
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

        private void GetSelection(GH_Document doc, string correlationId = null)
        {
            try
            {
                LogDebug($"GetSelection started. Doc null? {doc == null}");
                if (doc == null)
                {
                    LogDebug("No active Grasshopper document found");
                    SendErrorResponse("get_selection", correlationId, "No active Grasshopper document");
                    return;
                }

                // Get selected objects from the document
                var selectedObjects = new List<string>();

                // Grasshopper selection is available through the canvas
                var canvas = Grasshopper.Instances.ActiveCanvas;
                if (canvas != null && canvas.Document == doc)
                {
                    // Get selected objects from document
                    var docSelectedObjects = doc.SelectedObjects();
                    foreach (var selectedObj in docSelectedObjects)
                    {
                        if (selectedObj is IGH_DocumentObject docObj)
                        {
                            // Use standard hyphenated UUID format (36 characters)
                            var fullUuid = docObj.InstanceGuid.ToString();
                            selectedObjects.Add(fullUuid);
                        }
                    }
                }

                LogDebug($"Found {selectedObjects.Count} selected objects");

                // Send response with selected object UUIDs
                var responseData = new Dictionary<string, object>
                {
                    ["selectedIds"] = selectedObjects,
                    ["count"] = selectedObjects.Count,
                    ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                };

                SendSuccessResponseWithData("get_selection", correlationId,
                    $"Found {selectedObjects.Count} selected objects", responseData);
            }
            catch (Exception ex)
            {
                LogDebug($"GetSelection error: {ex.Message}");
                SendErrorResponse("get_selection", correlationId, $"Failed to get selection: {ex.Message}");
            }
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

                // Send response with slider UUID for future connections
                var responseData = new Dictionary<string, object>
                {
                    ["componentId"] = slider.InstanceGuid.ToString(),
                    ["componentUuid"] = slider.InstanceGuid.ToString(),
                    ["nickname"] = name
                };

                SendSuccessResponseWithData("create_slider", correlationId, $"Slider '{name}' created successfully", responseData);
            }
            catch (Exception ex)
            {
                var error = $"Failed to create slider: {ex.Message}";
                LogDebug(error);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error);
                SendErrorResponse("create_slider", correlationId, error);
            }
        }

        // ---------------------- Python Component Creation ----------------------


        // ---------------------- Direct Python Component Creation (Standalone Method) ----------------------

        private void CreatePythonComponent(GH_Document doc, IDictionary<string, object> payload, string correlationId)
        {
            try
            {
                var x = F(payload.TryGetValue("x", out var xv) ? xv : null, 20f);
                var y = F(payload.TryGetValue("y", out var yv) ? yv : null, 20f);
                var code = S(payload.TryGetValue("code", out var cv) ? cv : null,
                    @"# Standalone Python Component
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
second_output = result_second");

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

                LogDebug($"CreatePythonComponent: inputs={inputs.Count}, outputs={outputs.Count}, connections={connections.Count}");

                // Create the component using the proven method
                var pythonComponent = CreatePythonComponentAdvanced814(doc, x, y, code, inputs, outputs, connections);
                if (pythonComponent != null)
                {
                    var message = $"Python component created successfully with {inputs.Count} inputs, {outputs.Count} outputs, {connections.Count} connections";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, message);

                    // Send response with component UUID for future connections
                    var responseData = new Dictionary<string, object>
                    {
                        ["componentId"] = pythonComponent.InstanceGuid.ToString(),
                        ["componentUuid"] = pythonComponent.InstanceGuid.ToString(),
                        ["nickname"] = pythonComponent.NickName ?? "Python Component",
                        ["inputCount"] = inputs.Count,
                        ["outputCount"] = outputs.Count,
                        ["connectionCount"] = connections.Count
                    };

                    SendSuccessResponseWithData("create_python_component", correlationId, message, responseData);
                    return;
                }

                // Creation failed
                var error = "Python Component Creation Failed: RhinoCode API not available. Ensure RhinoCode plugin is loaded.";
                FallbackPanel(doc, x, y,
                    "Python Component Creation Failed.\n" +
                    "RhinoCode API not available.\n" +
                    "Ensure RhinoCode plugin is loaded.");

                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error);
                SendErrorResponse("create_python_component", correlationId, error);
            }
            catch (Exception ex)
            {
                var error = $"Failed to create Python component: {ex.Message}";
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error);
                LogDebug($"CreatePythonComponent exception: {ex}");
                SendErrorResponse("create_python_component", correlationId, error);
            }
        }

        private IGH_Component CreatePythonComponentAdvanced814(GH_Document doc, float x, float y, string code,
            List<Dictionary<string, object>> inputs, List<Dictionary<string, object>> outputs,
            List<Dictionary<string, object>> connections)
        {
            try
            {
                // Reference RhinoCodePluginGH.rhp and get required types
                var pythonComponentType = Type.GetType("RhinoCodePluginGH.Components.Python3Component, RhinoCodePluginGH");
                var scriptVariableParamType = Type.GetType("RhinoCodePluginGH.Parameters.ScriptVariableParam, RhinoCodePluginGH");

                if (pythonComponentType == null)
                {
                    LogDebug("RhinoCodePluginGH.Components.Python3Component type not found - API not available");
                    return null;
                }

                if (scriptVariableParamType == null)
                {
                    LogDebug("RhinoCodePluginGH.Parameters.ScriptVariableParam type not found - API not available");
                    return null;
                }

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
                    component = stringStringMethod.Invoke(null, new object[] { "StandalonePython", code });
                    LogDebug("Used Create(string, string) overload for standalone component");
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
                        component = singleStringMethod.Invoke(null, new object[] { "StandalonePython" });

                        // Set source code
                        var setSourceMethod = pythonComponentType.GetMethod("SetSource");
                        setSourceMethod?.Invoke(component, new object[] { code });
                        LogDebug("Used Create(string) overload with separate SetSource for standalone component");
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
                                component = stringBitmapBoolMethod.Invoke(null, new object[] { "StandalonePython", bmp, false });

                                // Set source code after creation
                                if (component != null)
                                {
                                    var setSourceMethod = pythonComponentType.GetMethod("SetSource");
                                    setSourceMethod?.Invoke(component, new object[] { code });
                                }
                            }
                            LogDebug("Used Create(string, Bitmap, bool) overload for standalone component");
                        }
                    }
                }

                if (component == null)
                {
                    LogDebug("Failed to create Python3Component instance using any overload");
                    return null;
                }

                var ghComponent = component as IGH_Component;
                if (ghComponent == null)
                {
                    LogDebug("Created component is not IGH_Component");
                    return null;
                }

                LogDebug($"Successfully created Python3Component, adding {inputs.Count} inputs and {outputs.Count} outputs");

                // Remove default inputs and outputs (x, y, out, a) before adding custom ones
                try
                {
                    // Clear default input parameters - collect first, then remove
                    var inputParamsToRemove = new List<IGH_Param>();
                    foreach (IGH_Param param in ghComponent.Params.Input)
                    {
                        inputParamsToRemove.Add(param);
                    }

                    foreach (var param in inputParamsToRemove)
                    {
                        var unregisterInputMethod = ghComponent.Params.GetType().GetMethod("UnregisterInputParameter", new Type[] { typeof(IGH_Param) });
                        if (unregisterInputMethod != null)
                        {
                            unregisterInputMethod.Invoke(ghComponent.Params, new object[] { param });
                            LogDebug($"Removed default input parameter: {param.Name}");
                        }
                    }

                    // Clear default output parameters - collect first, then remove
                    var outputParamsToRemove = new List<IGH_Param>();
                    foreach (IGH_Param param in ghComponent.Params.Output)
                    {
                        outputParamsToRemove.Add(param);
                    }

                    foreach (var param in outputParamsToRemove)
                    {
                        var unregisterOutputMethod = ghComponent.Params.GetType().GetMethod("UnregisterOutputParameter", new Type[] { typeof(IGH_Param) });
                        if (unregisterOutputMethod != null)
                        {
                            unregisterOutputMethod.Invoke(ghComponent.Params, new object[] { param });
                            LogDebug($"Removed default output parameter: {param.Name}");
                        }
                    }
                    LogDebug($"Successfully removed {inputParamsToRemove.Count} input and {outputParamsToRemove.Count} output default parameters");
                }
                catch (Exception ex)
                {
                    LogDebug($"Could not remove default parameters (continuing anyway): {ex.Message}");
                }

                // Add custom input parameters using the ScriptVariableParam approach
                foreach (var inputSpec in inputs)
                {
                    var name = S(inputSpec.TryGetValue("name", out var nameObj) ? nameObj : null, "input");
                    var nickname = S(inputSpec.TryGetValue("nickname", out var nickObj) ? nickObj : null, name);
                    var optional = inputSpec.TryGetValue("optional", out var optObj) && optObj is bool opt ? opt : true;
                    var access = S(inputSpec.TryGetValue("access", out var accessObj) ? accessObj : null, "item");

                    // Create ScriptVariableParam
                    var inputParam = Activator.CreateInstance(scriptVariableParamType, new object[] { name });
                    if (inputParam != null)
                    {
                        // Set properties
                        var prettyNameProp = scriptVariableParamType.GetProperty("PrettyName");
                        prettyNameProp?.SetValue(inputParam, nickname);

                        var toolTipProp = scriptVariableParamType.GetProperty("ToolTip");
                        toolTipProp?.SetValue(inputParam, $"Input parameter: {nickname}");

                        var optionalProp = scriptVariableParamType.GetProperty("Optional");
                        optionalProp?.SetValue(inputParam, optional);

                        var accessProp = scriptVariableParamType.GetProperty("Access");
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

                        var allowTreeAccessProp = scriptVariableParamType.GetProperty("AllowTreeAccess");
                        allowTreeAccessProp?.SetValue(inputParam, true);

                        // Set type hints if specified
                        if (inputSpec.TryGetValue("typeHint", out var typeHintObj))
                        {
                            var typeHint = S(typeHintObj);
                            var typeHintsProp = scriptVariableParamType.GetProperty("TypeHints");
                            var typeHints = typeHintsProp?.GetValue(inputParam);
                            if (typeHints != null)
                            {
                                // Try different type hint methods
                                if (typeHint == "double" || typeHint == "number")
                                {
                                    var selectMethod = typeHints.GetType().GetMethod("Select", new Type[] { typeof(Type) });
                                    selectMethod?.Invoke(typeHints, new object[] { typeof(double) });
                                }
                                else
                                {
                                    var selectMethod = typeHints.GetType().GetMethod("Select", new Type[] { typeof(string) });
                                    selectMethod?.Invoke(typeHints, new object[] { typeHint });
                                }
                            }
                        }

                        // CreateAttributes and register
                        var createAttribMethod = inputParam.GetType().GetMethod("CreateAttributes");
                        createAttribMethod?.Invoke(inputParam, null);

                        var registerInputMethod = ghComponent.Params.GetType().GetMethod("RegisterInputParam", new Type[] { typeof(IGH_Param) });
                        registerInputMethod?.Invoke(ghComponent.Params, new object[] { inputParam });

                        LogDebug($"Added input parameter: {name} ({nickname})");
                    }
                }

                // Add custom output parameters
                foreach (var outputSpec in outputs)
                {
                    var name = S(outputSpec.TryGetValue("name", out var nameObj) ? nameObj : null, "output");
                    var nickname = S(outputSpec.TryGetValue("nickname", out var nickObj) ? nickObj : null, name);
                    var hidden = outputSpec.TryGetValue("hidden", out var hiddenObj) && hiddenObj is bool h ? h : false;

                    var outputParam = Activator.CreateInstance(scriptVariableParamType, new object[] { name });
                    if (outputParam != null)
                    {
                        var prettyNameProp = scriptVariableParamType.GetProperty("PrettyName");
                        prettyNameProp?.SetValue(outputParam, nickname);

                        if (hidden)
                        {
                            var hiddenProp = scriptVariableParamType.GetProperty("Hidden");
                            hiddenProp?.SetValue(outputParam, true);
                        }

                        var createAttribMethod = outputParam.GetType().GetMethod("CreateAttributes");
                        createAttribMethod?.Invoke(outputParam, null);

                        var registerOutputMethod = ghComponent.Params.GetType().GetMethod("RegisterOutputParam", new Type[] { typeof(IGH_Param) });
                        registerOutputMethod?.Invoke(ghComponent.Params, new object[] { outputParam });

                        LogDebug($"Added output parameter: {name} ({nickname})");
                    }
                }

                // Call VariableParameterMaintenance
                var maintenanceMethod = pythonComponentType.GetMethod("VariableParameterMaintenance");
                if (maintenanceMethod != null)
                {
                    maintenanceMethod.Invoke(component, null);
                    LogDebug("Called VariableParameterMaintenance for standalone component");
                }
                else
                {
                    LogDebug("VariableParameterMaintenance method not found");
                }

                // Set special parameters
                var usingStandardOutputProp = pythonComponentType.GetProperty("UsingStandardOutputParam");
                usingStandardOutputProp?.SetValue(component, true);

                var graftStandardOutputProp = pythonComponentType.GetProperty("GraftStandardOutputLines");
                graftStandardOutputProp?.SetValue(component, true);

                var marshGuidsProp = pythonComponentType.GetProperty("MarshGuids");
                marshGuidsProp?.SetValue(component, false);

                // Position component and add to document
                ghComponent.CreateAttributes();
                ghComponent.Attributes.Pivot = new PointF(x, y);

                // Add to document safely
                try
                {
                    doc.AddObject(ghComponent, true);

                    // Make connections immediately after adding component (outside of solution context)
                    // This follows Grasshopper best practices for topology changes
                    if (connections != null && connections.Count > 0)
                    {
                        try
                        {
                            MakeConnections(doc, ghComponent, connections);

                            // Trigger a solution update after connections are made
                            ghComponent.ExpireSolution(true);
                            doc.NewSolution(false);
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"Connection failed for component: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"Error adding component to document: {ex.Message}");
                    return null;
                }

                return ghComponent;
            }
            catch (Exception ex)
            {
                LogDebug($"CreatePythonComponentAdvanced814 failed: {ex.Message}");
                return null;
            }
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

                    if (string.IsNullOrEmpty(sourceId))
                        continue;

                    // Find source object (component OR parameter like slider) with enhanced search strategy
                    IGH_DocumentObject sourceObject = null;
                    IGH_Param sourceParam = null;

                    // Strategy 1: Try as full GUID first
                    if (Guid.TryParse(sourceId, out var fullSourceGuid))
                    {
                        sourceObject = doc.FindObject(fullSourceGuid, true);
                    }

                    // Strategy 2: If not found, try as full GUID without hyphens
                    if (sourceObject == null && sourceId.Length == 32)
                    {
                        var formattedGuid = $"{sourceId.Substring(0, 8)}-{sourceId.Substring(8, 4)}-{sourceId.Substring(12, 4)}-{sourceId.Substring(16, 4)}-{sourceId.Substring(20, 12)}";
                        if (Guid.TryParse(formattedGuid, out var formattedSourceGuid))
                        {
                            sourceObject = doc.FindObject(formattedSourceGuid, true);
                        }
                    }

                    // Strategy 3: If still not found, try finding by nickname (components)
                    if (sourceObject == null)
                    {
                        sourceObject = doc.Objects.OfType<IGH_Component>()
                            .FirstOrDefault(c => c.NickName == sourceId);
                    }

                    // Strategy 4: If still not found, try finding by nickname (parameters/sliders)
                    if (sourceObject == null)
                    {
                        sourceObject = doc.Objects.OfType<IGH_Param>()
                            .FirstOrDefault(p => p.NickName == sourceId);
                    }

                    // Strategy 5: Try partial UUID matching
                    if (sourceObject == null && sourceId.Length >= 4)
                    {
                        sourceObject = doc.Objects
                            .FirstOrDefault(obj => obj.InstanceGuid.ToString().StartsWith(sourceId, StringComparison.OrdinalIgnoreCase));
                    }

                    // Determine source parameter for connection
                    if (sourceObject is IGH_Component sourceComponent)
                    {
                        // It's a component - get its output parameter
                        if (sourceOutput < sourceComponent.Params.Output.Count)
                        {
                            sourceParam = sourceComponent.Params.Output[sourceOutput];
                        }
                    }
                    else if (sourceObject is IGH_Param directParam)
                    {
                        // It's a parameter (like a slider) - use it directly
                        sourceParam = directParam;
                    }

                    if (sourceParam == null || sourceObject == null)
                        continue;

                    // Validate input index
                    if (targetInput >= targetComponent.Params.Input.Count)
                        continue;

                    // Make the connection
                    var targetParam = targetComponent.Params.Input[targetInput];
                    targetParam.AddSource(sourceParam);
                }
                catch (Exception ex)
                {
                    LogDebug($"Individual connection failed: {ex.Message}");
                    LogDebug($"Connection details: {JsonConvert.SerializeObject(connection)}");
                }
            }
        }

        // ---------------------- Wire Management (Connect/Disconnect) ----------------------

        private void ManageWires(GH_Document doc, Dictionary<string, object> payload, string correlationId)
        {
            try
            {
                var action = S(payload.TryGetValue("action", out var act) ? act : null, "connect");

                // Properly deserialize connections array (same pattern as CreatePythonComponent)
                var connections = payload.TryGetValue("connections", out var connectionsObj) ?
                    JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(JsonConvert.SerializeObject(connectionsObj)) ??
                    new List<Dictionary<string, object>>() : new List<Dictionary<string, object>>();

                // Properly deserialize partial operations array
                var partialOps = payload.TryGetValue("partialOperations", out var partialOpsObj) ?
                    JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(JsonConvert.SerializeObject(partialOpsObj)) ??
                    new List<Dictionary<string, object>>() : new List<Dictionary<string, object>>();

                int successCount = 0;
                int failureCount = 0;
                var errors = new List<string>();

                // Handle direct connections/disconnections
                if (connections.Count > 0)
                {
                    foreach (var connDict in connections)
                    {
                        var result = action.ToLower() == "connect"
                            ? ConnectWire(doc, connDict)
                            : DisconnectWire(doc, connDict);

                        if (result.success) successCount++;
                        else { failureCount++; errors.Add(result.error); }
                    }
                }

                // Handle partial operations (disconnect all from a param)
                if (partialOps.Count > 0)
                {
                    foreach (var opDict in partialOps)
                    {
                        var result = ExecutePartialOperation(doc, opDict);
                        if (result.success) successCount++;
                        else { failureCount++; errors.Add(result.error); }
                    }
                }

                // Trigger solution update
                doc.NewSolution(false);

                SendSuccessResponseWithData("manage_wires", correlationId, "Wire management completed", new Dictionary<string, object>
                {
                    ["successCount"] = successCount,
                    ["failureCount"] = failureCount,
                    ["errors"] = errors,
                    ["action"] = action
                });
            }
            catch (Exception ex)
            {
                SendErrorResponse("manage_wires", correlationId, ex.Message);
            }
        }

        private (bool success, string error) ConnectWire(GH_Document doc, Dictionary<string, object> connection)
        {
            var sourceUuid = S(connection.TryGetValue("sourceComponentUuid", out var src) ? src : null, "");
            var targetUuid = S(connection.TryGetValue("targetComponentUuid", out var tgt) ? tgt : null, "");
            var sourceOutputIndex = connection.TryGetValue("sourceOutputIndex", out var sOut) ? Convert.ToInt32(sOut) : 0;
            var targetInputIndex = connection.TryGetValue("targetInputIndex", out var tIn) ? Convert.ToInt32(tIn) : 0;

            if (!Guid.TryParse(sourceUuid, out var srcGuid) || !Guid.TryParse(targetUuid, out var tgtGuid))
                return (false, "Invalid UUID format");

            var sourceObj = doc.FindObject(srcGuid, true);
            var targetObj = doc.FindObject(tgtGuid, true);

            if (sourceObj == null || targetObj == null)
                return (false, "Component not found");

            // Get source parameter
            IGH_Param sourceParam = null;
            if (sourceObj is IGH_Component srcComp && sourceOutputIndex < srcComp.Params.Output.Count)
                sourceParam = srcComp.Params.Output[sourceOutputIndex];
            else if (sourceObj is IGH_Param srcParam)
                sourceParam = srcParam;

            if (sourceParam == null)
                return (false, "Source parameter not found");

            // Get target parameter
            IGH_Param targetParam = null;
            if (targetObj is IGH_Component tgtComp && targetInputIndex < tgtComp.Params.Input.Count)
                targetParam = tgtComp.Params.Input[targetInputIndex];

            if (targetParam == null)
                return (false, "Target parameter not found");

            // Make connection
            targetParam.AddSource(sourceParam);
            return (true, null);
        }

        private (bool success, string error) DisconnectWire(GH_Document doc, Dictionary<string, object> connection)
        {
            var sourceUuid = S(connection.TryGetValue("sourceComponentUuid", out var src) ? src : null, "");
            var targetUuid = S(connection.TryGetValue("targetComponentUuid", out var tgt) ? tgt : null, "");
            var sourceOutputIndex = connection.TryGetValue("sourceOutputIndex", out var sOut) ? Convert.ToInt32(sOut) : 0;
            var targetInputIndex = connection.TryGetValue("targetInputIndex", out var tIn) ? Convert.ToInt32(tIn) : 0;

            if (!Guid.TryParse(sourceUuid, out var srcGuid) || !Guid.TryParse(targetUuid, out var tgtGuid))
                return (false, "Invalid UUID format");

            var sourceObj = doc.FindObject(srcGuid, true);
            var targetObj = doc.FindObject(tgtGuid, true);

            if (sourceObj == null || targetObj == null)
                return (false, "Component not found");

            IGH_Param sourceParam = null;
            if (sourceObj is IGH_Component srcComp && sourceOutputIndex < srcComp.Params.Output.Count)
                sourceParam = srcComp.Params.Output[sourceOutputIndex];
            else if (sourceObj is IGH_Param srcParam)
                sourceParam = srcParam;

            if (sourceParam == null)
                return (false, "Source parameter not found");

            IGH_Param targetParam = null;
            if (targetObj is IGH_Component tgtComp && targetInputIndex < tgtComp.Params.Input.Count)
                targetParam = tgtComp.Params.Input[targetInputIndex];

            if (targetParam == null)
                return (false, "Target parameter not found");

            // Disconnect
            targetParam.RemoveSource(sourceParam);
            return (true, null);
        }

        private (bool success, string error) ExecutePartialOperation(GH_Document doc, Dictionary<string, object> operation)
        {
            var componentUuid = S(operation.TryGetValue("componentUuid", out var uuid) ? uuid : null, "");
            var paramType = S(operation.TryGetValue("parameterType", out var type) ? type : null, "input");
            var paramIndex = operation.TryGetValue("parameterIndex", out var idx) ? Convert.ToInt32(idx) : 0;
            var op = S(operation.TryGetValue("operation", out var oper) ? oper : null, "disconnect_all");

            if (!Guid.TryParse(componentUuid, out var guid))
                return (false, "Invalid UUID format");

            var obj = doc.FindObject(guid, true);
            if (obj == null)
                return (false, "Component not found");

            if (obj is IGH_Component comp)
            {
                var paramList = paramType.ToLower() == "input" ? comp.Params.Input : comp.Params.Output;
                if (paramIndex >= paramList.Count)
                    return (false, "Parameter index out of range");

                if (op == "disconnect_all")
                {
                    paramList[paramIndex].RemoveAllSources();
                    return (true, null);
                }
            }

            return (false, "Operation not supported");
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
                    const int MAX_MESSAGE_SIZE = 10000000; // 10MB limit
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
        public string FullUuidString { get; set; }  // Store full UUID for connections
        public float X { get; set; } // Component X position on canvas
        public float Y { get; set; } // Component Y position on canvas
        public string ComponentType { get; set; } // Type of component (e.g., Geometry, Curve, etc.)
        public List<PseudocodeInput> Inputs { get; set; } = new List<PseudocodeInput>();
        public List<PseudocodeOutput> Outputs { get; set; } = new List<PseudocodeOutput>();
    }

    public class PseudocodeInput
    {
        public string Name { get; set; }
        public string TypeName { get; set; }
        public List<int[]> Connections { get; set; } = new List<int[]>();
        public Guid ParameterUuid { get; set; } // Unique identifier for this parameter
        public bool IsOptional { get; set; } // Whether this parameter is optional/unused
        public bool HasConnections { get; set; } // Whether this input has any connections
    }

    public class PseudocodeOutput
    {
        public string Name { get; set; }
        public string TypeName { get; set; }
        public string DataPreview { get; set; }
        public bool HasRecipients { get; set; }
        public Guid ParameterUuid { get; set; } // Unique identifier for this parameter
    }
}
