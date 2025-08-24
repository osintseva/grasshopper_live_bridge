// LiveCodingGH/LiveCodingComponent.cs
// Simplified version - only handles connection, ping, slider, and python creation
using System;
using System.Collections.Generic;
using System.Drawing;
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