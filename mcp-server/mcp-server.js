#!/usr/bin/env node

import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ErrorCode,
  ListToolsRequestSchema,
  McpError,
} from "@modelcontextprotocol/sdk/types.js";
import WebSocket from 'ws';

class GrasshopperMCPServer {
  constructor() {
    this.server = new Server(
      {
        name: "grasshopper-mcp-server",
        version: "0.1.0",
      },
      {
        capabilities: {
          tools: {},
        },
      }
    );

    this.GH_WS_URL = 'ws://localhost:8181/live';
    this.setupToolHandlers();
  }

  /**
   * Connects to the Grasshopper WebSocket, sends a request,
   * and returns the parsed response.
   */
  async connectToGrasshopper(action, payload = {}) {
    return new Promise((resolve, reject) => {
      console.error(`[MCP] Connecting to Grasshopper at ${this.GH_WS_URL}...`);
      const ws = new WebSocket(this.GH_WS_URL);

      const timeout = setTimeout(() => {
        reject(new Error('Request timeout: get_canvas_info'));
        try { ws.close(); } catch {}
      }, 2000);

      ws.on('open', () => {
        console.error(`[MCP] Connection to Grasshopper established. Requesting ${action}...`);
        const request = {
          action: action,
          correlationId: `mcp-${Date.now()}`,
          payload: payload
        };
        ws.send(JSON.stringify(request));
      });

      ws.on('message', (data) => {
        try {
          const response = JSON.parse(data.toString());
          console.error(`[MCP] Received message: ${JSON.stringify(response)}`);
          
          if (response.action === `${action}_response`) {
            console.error(`[MCP] Response matches expected action: ${action}_response`);
            if (response.status === 'success') {
              console.error(`[MCP] ${action} SUCCESS response received!`);
              clearTimeout(timeout);
              ws.close();
              
              // Handle different response formats
              if (response.data && typeof response.data === 'string') {
                try {
                  resolve(JSON.parse(response.data));
                } catch {
                  resolve(response.data);
                }
              } else {
                resolve(response.data || response);
              }
            } else if (response.status === 'queued') {
              console.error(`[MCP] ${action} request QUEUED, continuing to wait...`);
              // Continue waiting for the success response
            } else if (response.status === 'error') {
              console.error(`[MCP] ${action} response ERROR: ${response.message}`);
              clearTimeout(timeout);
              ws.close();
              reject(new Error(`Grasshopper error: ${response.message || 'Unknown error'}`));
            } else {
              console.error(`[MCP] Unknown response status: ${response.status}`);
            }
          } else {
            console.error(`[MCP] Response action doesn't match. Expected: ${action}_response, Got: ${response.action}`);
          }
        } catch (err) {
          console.error(`[MCP] Failed to parse message: ${err.message}`);
          reject(new Error(`Failed to parse response from Grasshopper: ${err.message}`));
        }
      });

      ws.on('error', (err) => {
        console.error('[MCP] WebSocket error:', err);
        clearTimeout(timeout);
        reject(new Error(`Failed to connect to Grasshopper WebSocket at ${this.GH_WS_URL}. WebSocket error: ${err.message}. Is Rhino/Grasshopper running?`));
      });

      ws.on('close', () => {
        console.error('[MCP] Connection to Grasshopper closed.');
      });
    });
  }

  setupToolHandlers() {
    // List available tools (only those actually implemented in the C# server)
    this.server.setRequestHandler(ListToolsRequestSchema, async () => {
      return {
        tools: [
          {
            name: "get_canvas_state",
            description: "Get the current state of the Grasshopper canvas",
            inputSchema: {
              type: "object",
              properties: {},
              additionalProperties: false,
            },
          },
          {
            name: "ping",
            description: "Test connection to Grasshopper WebSocket server",
            inputSchema: {
              type: "object",
              properties: {},
              additionalProperties: false,
            },
          },
          {
            name: "create_slider",
            description: "Create a new number slider on the Grasshopper canvas",
            inputSchema: {
              type: "object",
              properties: {
                x: {
                  type: "number",
                  description: "X coordinate for the slider",
                  default: 150
                },
                y: {
                  type: "number", 
                  description: "Y coordinate for the slider",
                  default: 150
                },
                nickname: {
                  type: "string",
                  description: "Display name for the slider",
                  default: "From MCP"
                }
              },
              additionalProperties: false,
            },
          },
          {
            name: "create_python_script",
            description: "Create a new Python script component on the Grasshopper canvas",
            inputSchema: {
              type: "object",
              properties: {
                x: {
                  type: "number",
                  description: "X coordinate for the component",
                  default: 260
                },
                y: {
                  type: "number",
                  description: "Y coordinate for the component", 
                  default: 160
                },
                code: {
                  type: "string",
                  description: "Python code for the script",
                  default: "import datetime as _dt\nA = 'Python ready @ ' + _dt.datetime.now().strftime('%H:%M:%S')"
                }
              },
              additionalProperties: false,
            },
          },
          {
            name: "update_script",
            description: "Update the code in an existing script component",
            inputSchema: {
              type: "object",
              properties: {
                componentId: {
                  type: "string",
                  description: "GUID of the component to update"
                },
                code: {
                  type: "string",
                  description: "New Python code for the script"
                }
              },
              required: ["componentId", "code"],
              additionalProperties: false,
            },
          },
          {
            name: "hello_world",
            description: "Test connection with a hello world message",
            inputSchema: {
              type: "object",
              properties: {
                name: {
                  type: "string",
                  description: "Name to greet",
                  default: "World"
                }
              },
              additionalProperties: false,
            },
          },
        ],
      };
    });

    // Handle tool calls
    this.server.setRequestHandler(CallToolRequestSchema, async (request) => {
      try {
        const { name, arguments: args } = request.params;

        switch (name) {
          case "get_canvas_state":
            return await this.handleGetCanvasState(args || {});
          
          case "ping":
            return await this.handlePing(args || {});
          
          case "create_slider":
            return await this.handleCreateSlider(args || {});
          
          case "create_python_script":
            return await this.handleCreatePythonScript(args || {});
          
          case "update_script":
            return await this.handleUpdateScript(args || {});
          
          case "hello_world":
            return await this.handleHelloWorld(args || {});
          
          default:
            throw new McpError(
              ErrorCode.MethodNotFound,
              `Unknown tool: ${name}`
            );
        }
      } catch (error) {
        throw new McpError(
          ErrorCode.InternalError,
          `Tool execution failed: ${error.message}`
        );
      }
    });
  }

  async handleGetCanvasState(args) {
    try {
      // Use empty payload to match what the C# server expects
      const canvasData = await this.connectToGrasshopper('get_canvas_info', {});
      
      return {
        content: [
          {
            type: "text",
            text: JSON.stringify(canvasData, null, 2),
          },
        ],
      };
    } catch (error) {
      throw new McpError(ErrorCode.InternalError, `Failed to get canvas state: ${error.message}`);
    }
  }

  async handlePing(args) {
    try {
      const result = await this.connectToGrasshopper('ping', { t: Date.now() });
      
      return {
        content: [
          {
            type: "text",
            text: `Ping successful: ${JSON.stringify(result, null, 2)}`,
          },
        ],
      };
    } catch (error) {
      throw new McpError(ErrorCode.InternalError, `Ping failed: ${error.message}`);
    }
  }

  async handleCreateSlider(args) {
    try {
      const payload = {
        x: args.x || 150,
        y: args.y || 150,
        nickname: args.nickname || 'From MCP'
      };
      
      const result = await this.connectToGrasshopper('create_slider', payload);
      
      return {
        content: [
          {
            type: "text",
            text: `Slider created successfully: ${JSON.stringify(result, null, 2)}`,
          },
        ],
      };
    } catch (error) {
      throw new McpError(ErrorCode.InternalError, `Failed to create slider: ${error.message}`);
    }
  }

  async handleCreatePythonScript(args) {
    try {
      const payload = {
        x: args.x || 260,
        y: args.y || 160,
        code: args.code || "import datetime as _dt\nA = 'Python ready @ ' + _dt.datetime.now().strftime('%H:%M:%S')"
      };
      
      const result = await this.connectToGrasshopper('create_python_script', payload);
      
      return {
        content: [
          {
            type: "text", 
            text: `Python script created successfully: ${JSON.stringify(result, null, 2)}`,
          },
        ],
      };
    } catch (error) {
      throw new McpError(ErrorCode.InternalError, `Failed to create Python script: ${error.message}`);
    }
  }

  async handleUpdateScript(args) {
    try {
      if (!args.componentId || !args.code) {
        throw new Error("Both componentId and code are required");
      }
      
      const payload = {
        componentId: args.componentId,
        code: args.code
      };
      
      const result = await this.connectToGrasshopper('update_script', payload);
      
      return {
        content: [
          {
            type: "text",
            text: `Script updated successfully: ${JSON.stringify(result, null, 2)}`,
          },
        ],
      };
    } catch (error) {
      throw new McpError(ErrorCode.InternalError, `Failed to update script: ${error.message}`);
    }
  }


  async handleHelloWorld(args) {
    const name = args.name || 'World';
    return {
      content: [
        {
          type: "text",
          text: `Hello, ${name}! The Grasshopper MCP Server is working correctly.`,
        },
      ],
    };
  }

  async run() {
    const transport = new StdioServerTransport();
    await this.server.connect(transport);
    console.error("Grasshopper MCP Server running on stdio");
  }
}

const server = new GrasshopperMCPServer();
server.run().catch(console.error);