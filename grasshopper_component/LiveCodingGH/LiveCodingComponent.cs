// grasshopper_component/LiveCodingGH/LiveCodingComponent.cs
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Newtonsoft.Json;
using Rhino.Geometry;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace LiveCoding
{
    /// <summary>
    /// Live-coding controller for Python (Rhino 8 RhinoCode + legacy GhPython fallback).
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
                _server.AddWebSocketService("/live", () => new LiveCodingService(_queue)); // obsolete API is fine for POC
#pragma warning restore CS0618
                Task.Run(() => _server.Start());
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
                        ExpireSolution(true);
                    }
                    catch (Exception ex)
                    {
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
                    GetCanvasInfo(doc);
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

        private void GetCanvasInfo(GH_Document doc)
        {
            try
            {
                LogDebug($"GetCanvasInfo started. Doc null? {doc == null}");
                if (doc == null)
                {
                    LogDebug("No active Grasshopper document found");
                    return;
                }
                
                var definition = new CompactGhDefinition();
                var docObjects = doc.Objects.ToList();
                var guidToIndexMap = docObjects.Select((obj, i) => new { obj.InstanceGuid, i }).ToDictionary(x => x.InstanceGuid, x => x.i);

                for (int i = 0; i < docObjects.Count; i++)
                {
                    var obj = docObjects[i];
                    var myComponent = new CompactGhComponent { Id = i };

                    if (obj is IGH_Component ghComponent)
                    {
                        myComponent.Name = ghComponent.Name;
                        myComponent.NickName = ghComponent.NickName;
                        myComponent.Description = ghComponent.Description;
                        myComponent.IsComponent = true;
                        
                        foreach (var inputParam in ghComponent.Params.Input)
                        {
                            var connections = new List<int[]>();
                            foreach (var source in inputParam.Sources)
                            {
                                var sourceOwnerObj = source.Attributes.GetTopLevel.DocObject;
                                if (sourceOwnerObj != null && guidToIndexMap.TryGetValue(sourceOwnerObj.InstanceGuid, out int sourceId))
                                {
                                    int sourceParamIndex = (sourceOwnerObj is IGH_Component sourceComp) ? sourceComp.Params.Output.IndexOf(source) : 0;
                                    if(sourceParamIndex > -1) connections.Add(new int[] { sourceId, sourceParamIndex });
                                }
                            }
                            myComponent.Inputs.Add(new GhInputParameter { Name = inputParam.Name, TypeName = inputParam.TypeName, Connections = connections });
                        }

                        bool isSourceComponent = !ghComponent.Params.Input.Any(p => p.Sources.Any());
                        myComponent.Outputs = ghComponent.Params.Output.Select(p => {
                            bool isUnconnectedOutput = p.Recipients.Count == 0;
                            bool showFullData = isSourceComponent || isUnconnectedOutput;

                            var data = p.VolatileData.AllData(true);
                            var sb = new StringBuilder();
                            foreach (var goo in data)
                            {
                                if (sb.Length > 0) sb.Append(", ");
                                sb.Append(GetDataPreviewString(goo));
                                if (!showFullData && sb.Length > PREVIEW_CHAR_LIMIT)
                                {
                                    sb.Length = PREVIEW_CHAR_LIMIT;
                                    sb.Append("...");
                                    break;
                                }
                            }
                            string dataPreview = sb.ToString();
                            if (showFullData && dataPreview.Length > FULL_DATA_CHAR_LIMIT)
                            {
                                dataPreview = dataPreview.Substring(0, FULL_DATA_CHAR_LIMIT) + "...";
                            }

                            return new GhOutputParameter { Name = p.Name, TypeName = p.TypeName, DataPreview = dataPreview };
                        }).ToList();
                    }
                    else if (obj is IGH_Param ghParam)
                    {
                        myComponent.Name = ghParam.Name;
                        myComponent.NickName = ghParam.NickName;
                        myComponent.Description = ghParam.Description;
                        myComponent.IsComponent = false;

                        bool isSourceParam = ghParam.Sources.Count == 0;
                        bool isUnconnectedOutput = ghParam.Recipients.Count == 0;
                        bool showFullData = isSourceParam || isUnconnectedOutput;

                        var data = ghParam.VolatileData.AllData(true);
                        var sb = new StringBuilder();
                        foreach (var goo in data)
                        {
                            if (sb.Length > 0) sb.Append(", ");
                            sb.Append(GetDataPreviewString(goo));
                            if (!showFullData && sb.Length > PREVIEW_CHAR_LIMIT)
                            {
                                sb.Length = PREVIEW_CHAR_LIMIT;
                                sb.Append("...");
                                break;
                            }
                        }
                        string dataPreview = sb.ToString();
                        if (showFullData && dataPreview.Length > FULL_DATA_CHAR_LIMIT)
                        {
                            dataPreview = dataPreview.Substring(0, FULL_DATA_CHAR_LIMIT) + "...";
                        }

                        myComponent.Outputs.Add(new GhOutputParameter { Name = ghParam.Name, TypeName = ghParam.TypeName, DataPreview = dataPreview });

                        if (ghParam.Sources.Any())
                        {
                            var connections = new List<int[]>();
                            foreach(var source in ghParam.Sources)
                            {
                                var sourceOwnerObj = source.Attributes.GetTopLevel.DocObject;
                                if (sourceOwnerObj != null && guidToIndexMap.TryGetValue(sourceOwnerObj.InstanceGuid, out int sourceId))
                                {
                                    int sourceParamIndex = (sourceOwnerObj is IGH_Component sourceComp) ? sourceComp.Params.Output.IndexOf(source) : 0;
                                    if(sourceParamIndex > -1) connections.Add(new int[] { sourceId, sourceParamIndex });
                                }
                            }
                            myComponent.Inputs.Add(new GhInputParameter { Name = "Input", TypeName = ghParam.TypeName, Connections = connections });
                        }
                    }
                    definition.Components.Add(myComponent);
                }

                LogDebug($"Found {docObjects.Count} objects in document");
                
                string jsonResult = Newtonsoft.Json.JsonConvert.SerializeObject(definition, Newtonsoft.Json.Formatting.Indented);
                LogDebug($"JSON serialized, length: {jsonResult.Length}");
                
                // Broadcast the canvas info via WebSocket
                try
                {
                    BroadcastCanvasInfo(jsonResult);
                    LogDebug("Canvas info sent via WebSocket");
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

        private void BroadcastCanvasInfo(string jsonData)
        {
            LogDebug($"BroadcastCanvasInfo called. Server null? {_server == null}");
            
            if (_server?.WebSocketServices != null)
            {
                LogDebug($"WebSocketServices found: {_server.WebSocketServices.Count} services");
                
                var service = _server.WebSocketServices["/live"];
                LogDebug($"/live service null? {service == null}");
                
                if (service?.Sessions != null)
                {
                    LogDebug($"Sessions count: {service.Sessions.Count}");
                    
                    var response = new
                    {
                        action = "get_canvas_info_response",
                        correlationId = Guid.NewGuid().ToString(),
                        status = "success",
                        data = jsonData
                    };
                    
                    string responseJson = Newtonsoft.Json.JsonConvert.SerializeObject(response);
                    LogDebug($"Broadcasting response, length: {responseJson.Length}");
                    
                    service.Sessions.Broadcast(responseJson);
                    LogDebug("Broadcast completed");
                }
                else
                {
                    LogDebug("Service sessions is null");
                }
            }
            else
            {
                LogDebug("Server or WebSocketServices is null");
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
        // Rhino 8 path: RhinoCodePluginGH.Components.Python3Component.Create(...)
        // Overloads differ; we disambiguate by parameter types.
        // Legacy fallback: GhPython.Component.ZuiPythonComponent with property "Code"/etc.

        private void CreatePythonScript(GH_Document doc, IDictionary<string, object> payload)
        {
            var x = F(payload.TryGetValue("x", out var xv) ? xv : null, 260f);
            var y = F(payload.TryGetValue("y", out var yv) ? yv : null, 160f);
            var code = S(payload.TryGetValue("code", out var cv) ? cv : null,
                "import datetime as _dt\nA = 'Py ready @ ' + _dt.datetime.now().strftime('%H:%M:%S')");

            // Try Rhino 8 API first (RhinoCodePluginGH)
            var created = TryCreateRhino8Python(doc, x, y, code);
            if (created) return;

            // Fallback to legacy GhPython (Rhino 7 / legacy in Rhino 8)
            var ok = TryCreateLegacyGhPython(doc, x, y, code);
            if (!ok)
            {
                FallbackPanel(doc, x, y,
                    "Could not create a Python script component.\n" +
                    "Rhino 8: ensure RhinoCode plugin is loaded.\n" +
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

            // Rhino 8 API: prefer SetSource if this is a RhinoCode script component
            if (TrySetRhino8Source(obj, code))
            {
                obj.ExpireSolution(true);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Updated code on {obj.NickName} (Rhino 8).");
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
        public LiveCodingService(System.Collections.Concurrent.ConcurrentQueue<CommandMessage> q) => _queue = q;

        protected override void OnMessage(MessageEventArgs e)
        {
            try
            {
                var msg = JsonConvert.DeserializeObject<CommandMessage>(e.Data) ?? new CommandMessage();
                _queue.Enqueue(msg);
                var ack = new
                {
                    action = (msg.Action ?? "unknown") + "_response",
                    correlationId = msg.CorrelationId,
                    status = "queued"
                };
                Send(JsonConvert.SerializeObject(ack));
            }
            catch (Exception ex)
            {
                Send(JsonConvert.SerializeObject(new { action = "error", message = ex.Message }));
            }
        }
    }

    public class CommandMessage
    {
        [JsonProperty("action")] public string Action { get; set; }
        [JsonProperty("correlationId")] public string CorrelationId { get; set; }
        [JsonProperty("payload")] public Dictionary<string, object> Payload { get; set; }
    }

    // ---------------------- Canvas Analysis Data Models ----------------------

    public class CompactGhDefinition 
    { 
        public List<CompactGhComponent> Components { get; set; } = new List<CompactGhComponent>(); 
    }

    public class GhInputParameter 
    { 
        public string Name { get; set; } 
        public string TypeName { get; set; } 
        public List<int[]> Connections { get; set; } = new List<int[]>(); 
    }

    public class GhOutputParameter 
    { 
        public string Name { get; set; } 
        public string TypeName { get; set; } 
        public string DataPreview { get; set; } 
    }

    public class CompactGhComponent
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string NickName { get; set; }
        public string Description { get; set; }
        public bool IsComponent { get; set; }
        public List<GhInputParameter> Inputs { get; set; } = new List<GhInputParameter>();
        public List<GhOutputParameter> Outputs { get; set; } = new List<GhOutputParameter>();
    }
}
