// grasshopper_component/LiveCodingGH/LiveCodingComponent.cs
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
                return;
            }

            var payload = cmd.Payload ?? new Dictionary<string, object>();

            switch ((cmd.Action ?? string.Empty).ToLowerInvariant())
            {
                case "ping":
                    // no-op (the WS service already acks)
                    break;

                case "create_slider":
                    CreateSlider(doc, payload);
                    break;

                case "create_python_script":
                    CreatePythonScript(doc, payload);
                    break;

                case "update_script":
                    UpdateExistingScript(doc, payload);
                    break;

                case "get_canvas_info":
                    GetCanvasInfo(doc, cmd.CorrelationId);
                    break;

                case "create_python_advanced":
                    CreatePythonAdvanced(doc, payload);
                    break;

                case "create_python_xml":
                    CreatePythonXml(doc, payload);
                    break;

                default:
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Unknown action: {cmd.Action}");
                    break;
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

        private void CreateSlider(GH_Document doc, IDictionary<string, object> payload)
        {
            var x = F(payload.TryGetValue("x", out var xv) ? xv : null, 150f);
            var y = F(payload.TryGetValue("y", out var yv) ? yv : null, 150f);
            var name = S(payload.TryGetValue("nickname", out var nv) ? nv : null, "From VSCode");

            var slider = new GH_NumberSlider();
            slider.CreateAttributes();
            slider.Attributes.Pivot = new PointF(x, y);
            slider.NickName = name;

            doc.AddObject(slider, true);
        }

        // ---------------------- Create Python Script ----------------------
        // Rhino 7 path: RhinoCodePluginGH.Components.Python3Component.Create(...)
        // Overloads differ; we disambiguate by parameter types.
        // Legacy fallback: GhPython.Component.ZuiPythonComponent with property "Code"/etc.

        private void CreatePythonScript(GH_Document doc, IDictionary<string, object> payload)
        {
            var x = F(payload.TryGetValue("x", out var xv) ? xv : null, 260f);
            var y = F(payload.TryGetValue("y", out var yv) ? yv : null, 160f);
            var code = S(payload.TryGetValue("code", out var cv) ? cv : null,
                "import datetime as _dt\nA = 'Py ready @ ' + _dt.datetime.now().strftime('%H:%M:%S')");

            // Try Rhino 7 API first (RhinoCodePluginGH)
            var created = TryCreateRhino8Python(doc, x, y, code);
            if (created) return;

            // Fallback to legacy GhPython (Rhino 7 / legacy in Rhino 7)
            var ok = TryCreateLegacyGhPython(doc, x, y, code);
            if (!ok)
            {
                FallbackPanel(doc, x, y,
                    "Could not create a Python script component.\n" +
                    "Rhino 7: ensure RhinoCode plugin is loaded.\n" +
                    "Legacy: ensure GHPython is installed.");
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

        private void CreatePythonAdvanced(GH_Document doc, IDictionary<string, object> payload)
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

            try
            {
                // Try the official Rhino 8.14+ API
                var pythonComponent = CreateAdvancedPython814(doc, x, y, code, inputs, outputs, connections);
                if (pythonComponent != null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Advanced Python component created successfully with Rhino 8.14+ API");
                    return;
                }

                // Fallback message
                FallbackPanel(doc, x, y,
                    "Advanced Python Creation Failed.\n" +
                    "Rhino 8.14+ API not available.\n" +
                    "Try create_python_xml endpoint instead.");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to create advanced Python component: {ex.Message}");
                LogDebug($"CreatePythonAdvanced exception: {ex}");
            }
        }

        private IGH_Component CreateAdvancedPython814(GH_Document doc, float x, float y, string code,
            List<Dictionary<string, object>> inputs, List<Dictionary<string, object>> outputs,
            List<Dictionary<string, object>> connections)
        {
            // Check if the official API is available
            var pythonType = Type.GetType("RhinoCodePluginGH.Components.Python3Component, RhinoCodePluginGH");
            if (pythonType == null)
            {
                LogDebug("Python3Component type not found");
                return null;
            }

            var paramType = Type.GetType("RhinoCodePluginGH.Parameters.ScriptVariableParam, RhinoCodePluginGH");
            if (paramType == null)
            {
                LogDebug("ScriptVariableParam type not found");
                return null;
            }

            // Try to create using the new API
            var createMethod = pythonType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string), typeof(string) }, null);

            if (createMethod == null)
            {
                LogDebug("Python3Component.Create method not found");
                return null;
            }

            var component = createMethod.Invoke(null, new object[] { "AdvancedPython", code }) as IGH_Component;
            if (component == null)
            {
                LogDebug("Failed to create Python3Component instance");
                return null;
            }

            // Add custom inputs
            foreach (var inputSpec in inputs)
            {
                var name = S(inputSpec.TryGetValue("name", out var nameObj) ? nameObj : null, "input");
                var nickname = S(inputSpec.TryGetValue("nickname", out var nickObj) ? nickObj : null, name);
                var optional = inputSpec.TryGetValue("optional", out var optObj) && optObj is bool opt ? opt : true;
                var access = S(inputSpec.TryGetValue("access", out var accessObj) ? accessObj : null, "item");

                var inputParam = Activator.CreateInstance(paramType) as IGH_Param;
                if (inputParam != null)
                {
                    inputParam.Name = name;
                    inputParam.NickName = nickname;
                    inputParam.Optional = optional;

                    // Set access type
                    if (access.ToLower() == "list")
                        inputParam.Access = GH_ParamAccess.list;
                    else if (access.ToLower() == "tree")
                        inputParam.Access = GH_ParamAccess.tree;
                    else
                        inputParam.Access = GH_ParamAccess.item;

                    inputParam.CreateAttributes();
                    component.Params.RegisterInputParam(inputParam);
                }
            }

            // Add custom outputs
            foreach (var outputSpec in outputs)
            {
                var name = S(outputSpec.TryGetValue("name", out var nameObj) ? nameObj : null, "output");
                var nickname = S(outputSpec.TryGetValue("nickname", out var nickObj) ? nickObj : null, name);

                var outputParam = Activator.CreateInstance(paramType) as IGH_Param;
                if (outputParam != null)
                {
                    outputParam.Name = name;
                    outputParam.NickName = nickname;
                    outputParam.CreateAttributes();
                    component.Params.RegisterOutputParam(outputParam);
                }
            }

            // Finalize parameter changes
            var maintenanceMethod = pythonType.GetMethod("VariableParameterMaintenance");
            maintenanceMethod?.Invoke(component, null);

            // Position and add to document
            component.CreateAttributes();
            component.Attributes.Pivot = new PointF(x, y);
            doc.AddObject(component, true);

            // Handle connections after adding to document
            doc.ScheduleSolution(10, _ => {
                try
                {
                    MakeConnections(doc, component, connections);
                    component.ExpireSolution(true);
                }
                catch (Exception ex)
                {
                    LogDebug($"Connection failed: {ex.Message}");
                }
            });

            return component;
        }

        // ---------------------- XML-based Python Component Creation ----------------------

        private void CreatePythonXml(GH_Document doc, IDictionary<string, object> payload)
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

            try
            {
                var pythonComponent = CreatePythonWithXml(doc, x, y, code, inputs, outputs, connections);
                if (pythonComponent != null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "XML-based Python component created successfully");
                    return;
                }

                FallbackPanel(doc, x, y,
                    "XML Python Creation Failed.\n" +
                    "Neither RhinoCode nor GhPython available.\n" +
                    "Install Python component support.");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to create XML Python component: {ex.Message}");
                LogDebug($"CreatePythonXml exception: {ex}");
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

        private void UpdateExistingScript(GH_Document doc, IDictionary<string, object> payload)
        {
            var idStr = S(payload.TryGetValue("componentId", out var idv) ? idv : null, "");
            var code = S(payload.TryGetValue("code", out var cv) ? cv : null, "");

            if (!Guid.TryParse(idStr, out var id))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Bad componentId '{idStr}'");
                return;
            }

            var obj = doc.FindObject(id, true);
            if (obj == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Object {id} not found in document.");
                return;
            }

            // Rhino 7 API: prefer SetSource if this is a RhinoCode script component
            if (TrySetRhino8Source(obj, code))
            {
                obj.ExpireSolution(true);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Updated code on {obj.NickName} (Rhino 7).");
                return;
            }

            // Legacy fallback
            if (TrySetScriptCode(obj, code, out var dbg))
            {
                obj.ExpireSolution(true);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Updated code on {obj.NickName} (legacy).");
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Could not set code on {obj.GetType().FullName}.\n{dbg}");
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
