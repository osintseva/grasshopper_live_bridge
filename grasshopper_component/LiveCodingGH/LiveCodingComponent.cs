using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Rhino.Geometry;
using WebSocketSharp;
using WebSocketSharp.Server;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Windows.Forms;

namespace LiveCoding
{
    public class LiveCodingComponent : GH_Component
    {
        private WebSocketServer wsServer;
        private ConcurrentQueue<CommandMessage> commandQueue;
        private Timer processTimer;
        private const int WS_PORT = 8181;

        public LiveCodingComponent()
          : base("Live Coding Controller", "LiveCode",
              "WebSocket controller for live coding",
              "Params", "Util")
        {
            commandQueue = new ConcurrentQueue<CommandMessage>();
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // No inputs - this is a service component
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "S", "Server status", GH_ParamAccess.item);
            pManager.AddTextParameter("Last Command", "C", "Last received command", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Start server if not running
            if (wsServer == null)
            {
                StartServer();
                StartProcessingTimer();
            }

            string status = wsServer != null && wsServer.IsListening 
                ? $"Server running on ws://localhost:{WS_PORT}" 
                : "Server not running";
            
            DA.SetData(0, status);
            DA.SetData(1, LastCommandReceived ?? "None");
        }

        private string LastCommandReceived { get; set; }

        private void StartServer()
        {
            try
            {
                wsServer = new WebSocketServer(WS_PORT);
                wsServer.AddWebSocketService<LiveCodingService>("/live", () => 
                    new LiveCodingService(commandQueue));
                
                Task.Run(() => wsServer.Start());
                
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                    $"WebSocket server started on port {WS_PORT}");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, 
                    $"Failed to start server: {ex.Message}");
            }
        }

        private void StartProcessingTimer()
        {
            processTimer = new Timer();
            processTimer.Interval = 100; // Process queue every 100ms
            processTimer.Tick += ProcessCommandQueue;
            processTimer.Start();
        }

        private void ProcessCommandQueue(object sender, EventArgs e)
        {
            if (commandQueue.TryDequeue(out CommandMessage cmd))
            {
                LastCommandReceived = $"{cmd.Action} at {DateTime.Now:HH:mm:ss}";
                
                try
                {
                    ExecuteCommand(cmd);
                    ExpireSolution(true); // Trigger component update to show status
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, 
                        $"Command execution failed: {ex.Message}");
                }
            }
        }

        private void ExecuteCommand(CommandMessage cmd)
        {
            var doc = OnPingDocument();
            if (doc == null) return;

            switch (cmd.Action)
            {
                case "update_script":
                    UpdatePythonScript(doc, cmd.Payload);
                    break;
                    
                case "create_slider":
                    CreateSlider(doc, cmd.Payload);
                    break;
                    
                case "ping":
                    // Just a test command
                    break;
                    
                default:
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, 
                        $"Unknown command: {cmd.Action}");
                    break;
            }
        }

        private void UpdatePythonScript(GH_Document doc, Dictionary<string, object> payload)
        {
            if (!payload.ContainsKey("componentId") || !payload.ContainsKey("code"))
                return;
                
            string componentId = payload["componentId"].ToString();
            string code = payload["code"].ToString();
            
            // Find the Python component by ID
            foreach (var obj in doc.Objects)
            {
                if (obj.InstanceGuid.ToString() == componentId)
                {
                    // This is simplified - actual implementation depends on GHPython version
                    // You'll need to cast to the specific Python component type
                    if (obj.Name.Contains("Python"))
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                            $"Would update Python component {componentId}");
                        // Actual update logic here
                    }
                    break;
                }
            }
            
            doc.ScheduleSolution(10);
        }

        private void CreateSlider(GH_Document doc, Dictionary<string, object> payload)
        {
            // Create a number slider at specified location
            var slider = new Grasshopper.Kernel.Special.GH_NumberSlider();
            slider.CreateAttributes();
            
            float x = 100;
            float y = 100;
            string nickname = "Slider";
            
            if (payload.ContainsKey("x") && float.TryParse(payload["x"].ToString(), out float xVal))
                x = xVal;
            if (payload.ContainsKey("y") && float.TryParse(payload["y"].ToString(), out float yVal))
                y = yVal;
            if (payload.ContainsKey("nickname"))
                nickname = payload["nickname"].ToString();
            
            slider.Attributes.Pivot = new System.Drawing.PointF(x, y);
            slider.NickName = nickname;
            
            doc.AddObject(slider, true);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            // Clean shutdown
            processTimer?.Stop();
            processTimer?.Dispose();
            
            if (wsServer != null && wsServer.IsListening)
            {
                wsServer.Stop();
                wsServer = null;
            }
            
            base.RemovedFromDocument(document);
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("12345678-1234-1234-1234-123456789ABC");
    }

    // WebSocket service handler
    public class LiveCodingService : WebSocketBehavior
    {
        private ConcurrentQueue<CommandMessage> commandQueue;

        public LiveCodingService(ConcurrentQueue<CommandMessage> queue)
        {
            commandQueue = queue;
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            try
            {
                var cmd = JsonConvert.DeserializeObject<CommandMessage>(e.Data);
                commandQueue.Enqueue(cmd);
                
                // Send acknowledgment
                var response = new
                {
                    action = cmd.Action + "_response",
                    correlationId = cmd.CorrelationId,
                    status = "queued"
                };
                
                Send(JsonConvert.SerializeObject(response));
            }
            catch (Exception ex)
            {
                Send(JsonConvert.SerializeObject(new { error = ex.Message }));
            }
        }
    }

    // Message structure
    public class CommandMessage
    {
        public string Action { get; set; }
        public string CorrelationId { get; set; }
        public Dictionary<string, object> Payload { get; set; }
    }
}