// grasshopper_component/LiveCodingGH/LiveCodingComponent.cs
using System;
using System.Collections.Generic;
using System.Drawing;
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
    public class ParameterDefinition
    {
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("typehint")] public string TypeHint { get; set; } = "generic";
        [JsonProperty("access")] public string Access { get; set; } = "item";
    }

    public class CommandMessage
    {
        [JsonProperty("action")] public string Action { get; set; }
        [JsonProperty("correlationId")] public string CorrelationId { get; set; }
        [JsonProperty("payload")] public Dictionary<string, object> Payload { get; set; }
        [JsonProperty("param_definitions")] public List<ParameterDefinition> ParamDefinitions { get; set; }
    }
    
    public class LiveCodingComponent : GH_Component
    {
        private const int WS_PORT = 8181;
        private WebSocketServer _server;
        private readonly System.Collections.Concurrent.ConcurrentQueue<CommandMessage> _queue = new System.Collections.Concurrent.ConcurrentQueue<CommandMessage>();
        private Timer _pump;

        public LiveCodingComponent()
            : base("Live Coding Controller (Python)", "LivePy", "WebSocket controller for live coding Python in Grasshopper", "Params", "Util") { }

        protected override void RegisterInputParams(GH_InputParamManager pManager) { }
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "S", "Server status", GH_ParamAccess.item);
            pManager.AddTextParameter("Last Command", "C", "Last received command", GH_ParamAccess.item);
        }

        private string LastCommand { get; set; } = "—";

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (_server == null) { StartServer(); StartPump(); }
            DA.SetData(0, _server?.IsListening == true ? $"Server: ws://localhost:{WS_PORT}/live (OK)" : "Server not running");
            DA.SetData(1, LastCommand);
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
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"WS start failed: {ex.Message}"); }
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
                    catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Command failed: {ex.Message}"); }
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
                case "ping": break;
                case "create_slider": CreateSlider(doc, payload); break;
                case "create_python_with_io": CreatePythonWithIO(doc, payload, cmd.ParamDefinitions); break;
                case "update_script": UpdateExistingScript(doc, payload, cmd.ParamDefinitions); break;
                default: AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Unknown action: {cmd.Action}"); break;
            }
        }

        private static float F(object o, float fallback) { try { return o == null ? fallback : Convert.ToSingle(o); } catch { return fallback; } }
        private static string S(object o, string fallback = "") => o == null ? fallback : o.ToString();
        private static readonly BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy | BindingFlags.IgnoreCase;
        
        private void CreateSlider(GH_Document doc, IDictionary<string, object> payload)
        {
            var slider = new GH_NumberSlider();
            slider.CreateAttributes();
            slider.Attributes.Pivot = new PointF(F(payload.TryGetValue("x", out var xv) ? xv : null, 150f), F(payload.TryGetValue("y", out var yv) ? yv : null, 150f));
            slider.NickName = S(payload.TryGetValue("nickname", out var nv) ? nv : null, "From VSCode");
            doc.AddObject(slider, true);
        }

        private void CreatePythonWithIO(GH_Document doc, IDictionary<string, object> payload, List<ParameterDefinition> paramDefs)
        {
            var x = F(payload.TryGetValue("x", out var xv) ? xv : null, 260f);
            var y = F(payload.TryGetValue("y", out var yv) ? yv : null, 160f);
            var code = S(payload.TryGetValue("code", out var cv) ? cv : null, "a = 'Ready'");

            if (!TryCreateRhino8PythonObject(code, out var component) && !TryCreateLegacyGhPythonObject(code, out component))
            {
                FallbackPanel(doc, x, y, "Could not create a Python script component.");
                return;
            }
            if (paramDefs?.Any() == true) { UpdateComponentParameters(component, paramDefs); }
            component.CreateAttributes();
            component.Attributes.Pivot = new PointF(x, y);
            doc.AddObject(component, true);
            doc.ScheduleSolution(1, _ => component.ExpireSolution(true));
        }

        private void UpdateComponentParameters(IGH_DocumentObject component, List<ParameterDefinition> paramDefs)
        {
            if (!(component is IGH_Component ghComp)) return;
            var compType = ghComp.GetType();
            for (int i = ghComp.Params.Input.Count - 1; i >= 1; i--) { ghComp.Params.UnregisterInputParameter(ghComp.Params.Input[i], true); }
            for (int i = ghComp.Params.Output.Count - 1; i >= 1; i--) { ghComp.Params.UnregisterOutputParameter(ghComp.Params.Output[i], true); }
            ghComp.Params.OnParametersChanged();
            bool isLegacy = compType.FullName.Contains("GhPython");
            foreach (var pDef in paramDefs)
            {
                if (pDef.Type.ToLower() == "input")
                {
                    if (isLegacy) { compType.GetMethod("Menu_AddInput", BF)?.Invoke(ghComp, null); }
                    else { compType.GetMethod("Menu_CreateParameter", BF, null, new[] { typeof(int) }, null)?.Invoke(ghComp, new object[] { -1 }); }
                    var newParam = ghComp.Params.Input.LastOrDefault();
                    if (newParam != null)
                    {
                        newParam.NickName = pDef.Name;
                        if (Enum.TryParse<GH_ParamAccess>(pDef.Access, true, out var access)) { newParam.Access = access; }
                    }
                }
                else if (pDef.Type.ToLower() == "output")
                {
                    if (isLegacy) { compType.GetMethod("Menu_AddOutput", BF)?.Invoke(ghComp, null); }
                    else { compType.GetMethod("Menu_CreateParameter", BF, null, new[] { typeof(int) }, null)?.Invoke(ghComp, new object[] { -1 }); }
                    var newParam = ghComp.Params.Output.LastOrDefault();
                    if (newParam != null) { newParam.NickName = pDef.Name; }
                }
            }
            ghComp.Params.OnParametersChanged();
            ghComp.ExpireSolution(true);
        }

        // RESTORED: Your original robust logic for creating the Rhino 8 component.
        private bool TryCreateRhino8PythonObject(string code, out IGH_DocumentObject obj)
        {
            obj = null;
            var t = Type.GetType("RhinoCodePluginGH.Components.Python3Component, RhinoCodePluginGH");
            if (t == null) return false;
            var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "Create").ToArray();
            var createStringString = methods.FirstOrDefault(m => { var ps = m.GetParameters(); return ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string); });
            if (createStringString != null)
            {
                obj = createStringString.Invoke(null, new object[] { "LivePython", code }) as IGH_DocumentObject;
            }
            else
            {
                var createStringBitmapBool = methods.FirstOrDefault(m => { var ps = m.GetParameters(); return ps.Length == 3 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(Bitmap) && ps[2].ParameterType == typeof(bool); });
                if (createStringBitmapBool != null)
                {
                    using (var bmp = new Bitmap(24, 24))
                    {
                        obj = createStringBitmapBool.Invoke(null, new object[] { "LivePython", bmp, false }) as IGH_DocumentObject;
                        if (obj != null) { TrySetRhino8Source(obj, code); }
                    }
                }
            }
            return obj != null;
        }

        private bool TryCreateLegacyGhPythonObject(string code, out IGH_DocumentObject obj)
        {
            obj = null;
            var t = Type.GetType("GhPython.Component.ZuiPythonComponent, GhPython");
            if (t == null) return false;
            obj = Activator.CreateInstance(t) as IGH_DocumentObject;
            if (obj != null) { TrySetScriptCode(obj, code, out _); }
            return obj != null;
        }

        private void UpdateExistingScript(GH_Document doc, IDictionary<string, object> payload, List<ParameterDefinition> paramDefs)
        {
            if (!Guid.TryParse(S(payload.TryGetValue("componentId", out var idv) ? idv : null), out var id)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Bad componentId"); return; }
            var obj = doc.FindObject(id, true);
            if (obj == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Object {id} not found."); return; }
            var code = S(payload.TryGetValue("code", out var cv) ? cv : null);
            if (TrySetRhino8Source(obj, code) || TrySetScriptCode(obj, code, out _)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Updated code on {obj.NickName}."); } 
            else { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Could not set code on {obj.GetType().FullName}."); }
            if (paramDefs?.Any() == true)
            {
                UpdateComponentParameters(obj, paramDefs);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Updated parameters on {obj.NickName}.");
            }
            obj.ExpireSolution(true);
        }

        private bool TrySetRhino8Source(IGH_DocumentObject obj, string code)
        {
            var mi = obj.GetType().GetMethod("SetSource", new[] { typeof(string) });
            if (mi == null) return false;
            try { mi.Invoke(obj, new object[] { code }); return true; } catch { return false; }
        }

        // RESTORED: Your original robust logic for injecting code into legacy components.
        private static bool TrySetScriptCode(object target, string code, out string debug)
        {
            debug = $"Target: {target.GetType().FullName}";
            var t = target.GetType();
            var propNames = new[] { "ScriptSource", "SourceCode", "Code", "Text", "Source" };
            foreach (var name in propNames)
            {
                var p = t.GetProperty(name, BF);
                if (p != null && p.CanWrite && p.PropertyType == typeof(string)) { p.SetValue(target, code); return true; }
            }
            var fieldNames = new[] { "m_py_code", "m_script", "_script", "_code", "m_source" };
            foreach (var name in fieldNames)
            {
                var f = t.GetField(name, BF);
                if (f != null && f.FieldType == typeof(string)) { f.SetValue(target, code); return true; }
            }
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
            if (_server?.IsListening == true) { try { _server.Stop(); } catch { } _server = null; }
            base.RemovedFromDocument(document);
        }

        protected override Bitmap Icon => null;
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
                Send(JsonConvert.SerializeObject(new { action = $"{msg.Action}_response", correlationId = msg.CorrelationId, status = "queued" }));
            }
            catch (Exception ex) { Send(JsonConvert.SerializeObject(new { action = "error", message = ex.Message })); }
        }
    }
}