// LiveCodingGH/LiveCodingComponent.cs
// Simplified version - only handles connection, ping, slider, and python creation
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Newtonsoft.Json;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace LiveCoding
{
    public class CommandMessage
    {
        [JsonProperty("action")] public string Action { get; set; }
        [JsonProperty("correlationId")] public string CorrelationId { get; set; }
        [JsonProperty("payload")] public Dictionary<string, object> Payload { get; set; }
    }

    public class LiveCodingComponent : GH_Component
    {
        private const int WS_PORT = 8181;
        private WebSocketServer _server;
        private readonly System.Collections.Concurrent.ConcurrentQueue<CommandMessage> _queue =
            new System.Collections.Concurrent.ConcurrentQueue<CommandMessage>();
        private Timer _pump;

        public LiveCodingComponent()
            : base("Live Coding Controller", "LiveCode",
                "WebSocket controller for live coding in Grasshopper", "Params", "Util")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager) { }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "S", "Server status", GH_ParamAccess.item);
            pManager.AddTextParameter("Last Command", "C", "Last received command", GH_ParamAccess.item);
            pManager.AddTextParameter("Component IDs", "IDs", "Created component IDs", GH_ParamAccess.list);
        }

        private string LastCommand { get; set; } = "—";
        private readonly List<string> ComponentIds { get; set; } = new List<string>();

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (_server == null) { StartServer(); StartPump(); }
            DA.SetData(0, _server?.IsListening == true ? $"Server: ws://localhost:{WS_PORT}/live (OK)" : "Server not running");
            DA.SetData(1, LastCommand);
            DA.SetDataList(2, ComponentIds);
        }

        private void StartServer()
        {
            try
            {
                _server = new WebSocketServer(WS_PORT);
                _server.AddWebSocketService("/live", () => new LiveCodingService(_queue));
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
        }

        private void Execute(CommandMessage cmd)
        {
            var doc = Grasshopper.Instances.ActiveCanvas?.Document;
            if (doc == null) return;

            var payload = cmd.Payload ?? new Dictionary<string, object>();
            switch ((cmd.Action ?? string.Empty).ToLowerInvariant())
            {
                case "ping":
                    // Just acknowledge - LastCommand already updated
                    break;
                case "create_slider":
                    CreateSlider(doc, payload);
                    break;
                case "create_python":
                    CreatePython(doc, payload);
                    break;
                case "connect_components":
                    ConnectComponents(doc, payload);
                    break;
                case "list_components":
                    ListComponents(doc, payload);
                    break;
                default:
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Unknown action: {cmd.Action}");
                    break;
            }
        }

        private static float F(object o, float fallback)
        {
            try { return o == null ? fallback : Convert.ToSingle(o); }
            catch { return fallback; }
        }

        private static string S(object o, string fallback = "") => o == null ? fallback : o.ToString();

        private void CreateSlider(GH_Document doc, IDictionary<string, object> payload)
        {
            var x = F(payload.TryGetValue("x", out var xv) ? xv : null, 100f);
            var y = F(payload.TryGetValue("y", out var yv) ? yv : null, 100f);
            var min = F(payload.TryGetValue("min", out var minv) ? minv : null, 0f);
            var max = F(payload.TryGetValue("max", out var maxv) ? maxv : null, 10f);
            var value = F(payload.TryGetValue("value", out var vv) ? vv : null, 5f);
            var name = S(payload.TryGetValue("name", out var nv) ? nv : null, "Slider");

            var slider = new GH_NumberSlider();
            slider.CreateAttributes();
            slider.Attributes.Pivot = new PointF(x, y);
            slider.NickName = name;
            slider.Slider.Minimum = (decimal)min;
            slider.Slider.Maximum = (decimal)max;
            slider.Slider.Value = (decimal)value;
            doc.AddObject(slider, true);
        }

        private void CreatePython(GH_Document doc, IDictionary<string, object> payload)
        {
            var x = F(payload.TryGetValue("x", out var xv) ? xv : null, 260f);
            var y = F(payload.TryGetValue("y", out var yv) ? yv : null, 160f);
            var code = S(payload.TryGetValue("code", out var cv) ? cv : null, "# IronPython script\na = 'Ready'");
            var scriptName = S(payload.TryGetValue("script_name", out var sn) ? sn : null, "Python Component");

            var component = CreateBlankLegacyGhPythonObject();
            if (component == null)
            {
                FallbackPanel(doc, x, y, "Could not create IronPython component. Is GHPython for Rhino 7 installed?");
                return;
            }

            TrySetScriptCode(component, code);

            component.CreateAttributes();
            component.Attributes.Pivot = new PointF(x, y);
            doc.AddObject(component, true);
            doc.ScheduleSolution(1, _ => component.ExpireSolution(true));

            // Save component info to file and add to ComponentIds list
            SaveComponentInfo(component, scriptName, code);
            ComponentIds.Add($"{scriptName}: {component.InstanceGuid}");
            
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                $"Created {scriptName} (ID: {component.InstanceGuid}) - saved to components log");
        }

        private IGH_DocumentObject CreateBlankLegacyGhPythonObject()
        {
            // GHPython for Rhino 7 (IronPython)
            var t = Type.GetType("GhPython.Component.ZuiPythonComponent, GhPython");
            if (t == null) return null;
            return Activator.CreateInstance(t) as IGH_DocumentObject;
        }

        private static bool TrySetScriptCode(object target, string code)
        {
            var t = target.GetType();
            var propNames = new[] { "ScriptSource", "SourceCode", "Code" };
            foreach (var name in propNames)
            {
                var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | 
                                      BindingFlags.NonPublic | BindingFlags.FlattenHierarchy | 
                                      BindingFlags.IgnoreCase);
                if (p != null && p.CanWrite && p.PropertyType == typeof(string))
                {
                    p.SetValue(target, code);
                    return true;
                }
            }
            return false;
        }

        private void SaveComponentInfo(IGH_DocumentObject component, string scriptName, string code)
        {
            try
            {
                // Try multiple possible locations
                string[] possiblePaths = {
                    Path.Combine(Environment.CurrentDirectory, "current_gh_components.md"),
                    Path.Combine(@"C:\Work\grasshopper_live_bridge", "current_gh_components.md"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "current_gh_components.md")
                };

                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var componentInfo = $@"
## {scriptName}
- **ID**: `{component.InstanceGuid}`
- **Type**: {component.GetType().Name}
- **Created**: {timestamp}
- **Position**: ({component.Attributes.Pivot.X}, {component.Attributes.Pivot.Y})
- **Script**: {scriptName}

";

                string successPath = null;
                Exception lastError = null;

                // Try each path until one works
                foreach (var logPath in possiblePaths)
                {
                    try
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Trying to save to: {logPath}");

                        // Create directory if it doesn't exist
                        var directory = Path.GetDirectoryName(logPath);
                        if (!Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        // Append to file (create if doesn't exist)
                        if (!File.Exists(logPath))
                        {
                            var header = $@"# Current Grasshopper Components Log
Generated automatically when creating components via WebSocket

Last Updated: {timestamp}

---

";
                            File.WriteAllText(logPath, header);
                        }
                        
                        File.AppendAllText(logPath, componentInfo);
                        successPath = logPath;
                        break; // Success, exit loop
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        continue; // Try next path
                    }
                }

                if (successPath != null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                        $"✓ Component {scriptName} (ID: {component.InstanceGuid}) saved to: {successPath}");
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, 
                        $"✗ Failed to save component info. Last error: {lastError?.Message}");
                    
                    // At least show the ID in the message
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                        $"COMPONENT ID: {scriptName} = {component.InstanceGuid}");
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Logging error: {ex.Message}");
                // Always show the ID even if logging fails
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                    $"COMPONENT ID: {scriptName} = {component.InstanceGuid}");
            }
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

        private void ConnectComponents(GH_Document doc, IDictionary<string, object> payload)
        {
            try
            {
                // Parse connection parameters
                var sourceId = S(payload.TryGetValue("source_id", out var sid) ? sid : null);
                var targetId = S(payload.TryGetValue("target_id", out var tid) ? tid : null);
                var sourceOutput = S(payload.TryGetValue("source_output", out var so) ? so : null, "0");
                var targetInput = S(payload.TryGetValue("target_input", out var ti) ? ti : null, "0");

                if (string.IsNullOrEmpty(sourceId) || string.IsNullOrEmpty(targetId))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Missing source_id or target_id for connection");
                    return;
                }

                // Find components by ID
                if (!Guid.TryParse(sourceId, out var sourceGuid))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Invalid source_id: {sourceId}");
                    return;
                }

                if (!Guid.TryParse(targetId, out var targetGuid))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Invalid target_id: {targetId}");
                    return;
                }

                var sourceObj = doc.FindObject(sourceGuid, true);
                var targetObj = doc.FindObject(targetGuid, true);

                if (sourceObj == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Source component not found: {sourceId}");
                    return;
                }

                if (targetObj == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Target component not found: {targetId}");
                    return;
                }

                if (!(sourceObj is IGH_Component sourceComp) || !(targetObj is IGH_Component targetComp))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Source or target is not a component");
                    return;
                }

                // Parse parameter indices (support both name and index)
                int sourceIndex = GetOutputParameterIndex(sourceComp, sourceOutput);
                int targetIndex = GetInputParameterIndex(targetComp, targetInput);

                if (sourceIndex < 0 || sourceIndex >= sourceComp.Params.Output.Count)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Invalid source output: {sourceOutput}");
                    return;
                }

                if (targetIndex < 0 || targetIndex >= targetComp.Params.Input.Count)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Invalid target input: {targetInput}");
                    return;
                }

                // Make the connection
                var sourceParam = sourceComp.Params.Output[sourceIndex];
                var targetParam = targetComp.Params.Input[targetIndex];

                targetParam.AddSource(sourceParam);
                
                // Trigger solution update
                targetComp.ExpireSolution(true);
                
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                    $"Connected {sourceComp.NickName}.{sourceParam.NickName} → {targetComp.NickName}.{targetParam.NickName}");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Connection failed: {ex.Message}");
            }
        }

        private int GetOutputParameterIndex(IGH_Component component, string identifier)
        {
            // Try to parse as integer index first
            if (int.TryParse(identifier, out var index))
            {
                return index;
            }

            // Try to find by name/nickname
            for (int i = 0; i < component.Params.Output.Count; i++)
            {
                var param = component.Params.Output[i];
                if (string.Equals(param.Name, identifier, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(param.NickName, identifier, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1; // Not found
        }

        private int GetInputParameterIndex(IGH_Component component, string identifier)
        {
            // Try to parse as integer index first
            if (int.TryParse(identifier, out var index))
            {
                return index;
            }

            // Try to find by name/nickname
            for (int i = 0; i < component.Params.Input.Count; i++)
            {
                var param = component.Params.Input[i];
                if (string.Equals(param.Name, identifier, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(param.NickName, identifier, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1; // Not found
        }

        private void ListComponents(GH_Document doc, IDictionary<string, object> payload)
        {
            try
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "=== Grasshopper Components ===");
                
                foreach (var obj in doc.Objects)
                {
                    if (obj is IGH_Component comp)
                    {
                        var inputs = string.Join(", ", comp.Params.Input.Select(p => p.NickName));
                        var outputs = string.Join(", ", comp.Params.Output.Select(p => p.NickName));
                        
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                            $"{comp.NickName} (ID: {comp.InstanceGuid})");
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                            $"  Inputs: [{inputs}]");
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                            $"  Outputs: [{outputs}]");
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "");
                    }
                }
                
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "=== End Component List ===");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"List components failed: {ex.Message}");
            }
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            try { _pump?.Stop(); _pump?.Dispose(); } catch { /* ignore */ }
            if (_server?.IsListening == true)
            {
                try { _server.Stop(); } catch { /* ignore */ }
                _server = null;
            }
            base.RemovedFromDocument(document);
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("4A5F8E6B-6F2E-4F92-A3B5-6B1C7C0D5B42");
    }

    public class LiveCodingService : WebSocketBehavior
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<CommandMessage> _queue;
        public LiveCodingService(System.Collections.Concurrent.ConcurrentQueue<CommandMessage> q) => _queue = q;

        protected override void OnMessage(MessageEventArgs e)
        {
            try
            {
                var msg = JsonConvert.DeserializeObject<CommandMessage>(e.Data);
                _queue.Enqueue(msg);
                Send(JsonConvert.SerializeObject(new { 
                    action = $"{msg.Action}_response", 
                    correlationId = msg.CorrelationId, 
                    status = "queued" 
                }));
            }
            catch (Exception ex)
            {
                Send(JsonConvert.SerializeObject(new { action = "error", message = ex.Message }));
            }
        }
    }
}