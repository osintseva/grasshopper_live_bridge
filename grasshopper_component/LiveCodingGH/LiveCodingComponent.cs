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
    /// <summary>
    /// Live-coding controller for Python (Rhino 8 RhinoCode + legacy GhPython fallback).
    /// WebSocket: ws://localhost:8181/live
    /// Actions:
    ///   - ping
    ///   - create_slider                      { x?, y?, nickname? }
    ///   - create_python_script               { x?, y?, code? }
    ///   - update_script                      { componentId, code }
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
        }

        private string LastCommand { get; set; } = "—";

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

        // ---------------------- Command dispatcher ----------------------

        private void Execute(CommandMessage cmd)
        {
            var doc = Grasshopper.Instances.ActiveCanvas?.Document;
            if (doc == null) return;

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
}
