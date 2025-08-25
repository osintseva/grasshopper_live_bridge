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
        reject(new Error('Timeout: Could not get a response from Grasshopper. Is the component on the canvas?'));
        try { ws.close(); } catch {}
      }, 10000);

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
          if (response.action === `${action}_response` && response.status === 'success') {
            console.error(`[MCP] ${action} response received successfully.`);
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
          } else if (response.status === 'error') {
            reject(new Error(`Grasshopper error: ${response.message || 'Unknown error'}`));
          }
        } catch (err) {
          reject(new Error(`Failed to parse response from Grasshopper: ${err.message}`));
        }
      });

      ws.on('error', (err) => {
        console.error('[MCP] WebSocket error:', err.message);
        clearTimeout(timeout);
        reject(new Error(`Failed to connect to Grasshopper WebSocket at ${this.GH_WS_URL}. Is Rhino/Grasshopper running?`));
      });

      ws.on('close', () => {
        console.error('[MCP] Connection to Grasshopper closed.');
      });
    });
  }

  setupToolHandlers() {
    // List available tools
    this.server.setRequestHandler(ListToolsRequestSchema, async () => {
      return {
        tools: [
          {
            name: "get_canvas_state",
            description: "Get the current state of the Grasshopper canvas",
            inputSchema: {
              type: "object",
              properties: {
                includeSelection: {
                  type: "boolean",
                  description: "Whether to include selection information",
                  default: false
                }
              },
              additionalProperties: false,
            },
          },
          {
            name: "get_selection", 
            description: "Get the current selection in Grasshopper",
            inputSchema: {
              type: "object",
              properties: {},
              additionalProperties: false,
            },
          },
          {
            name: "generate_ai_overview",
            description: "Generate an AI overview of the current Grasshopper definition",
            inputSchema: {
              type: "object", 
              properties: {},
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
          
          case "get_selection":
            return await this.handleGetSelection(args || {});
          
          case "generate_ai_overview":
            return await this.handleGenerateAIOverview(args || {});
          
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
      const canvasData = await this.connectToGrasshopper('get_canvas_info', {
        includeSelection: args.includeSelection || false
      });
      
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

  async handleGetSelection(args) {
    try {
      const selectionData = await this.connectToGrasshopper('get_selection');
      
      return {
        content: [
          {
            type: "text", 
            text: JSON.stringify(selectionData, null, 2),
          },
        ],
      };
    } catch (error) {
      throw new McpError(ErrorCode.InternalError, `Failed to get selection: ${error.message}`);
    }
  }

  async handleGenerateAIOverview(args) {
    try {
      // First get the canvas data
      const canvasData = await this.connectToGrasshopper('get_canvas_info');
      
      // Generate overview (using the same logic as your HTTP server)
      const componentCount = canvasData.Components ? canvasData.Components.length : 0;
      const componentNames = canvasData.Components ? 
        canvasData.Components.map(c => `\`${c.NickName}\` (${c.Name})`).join(', ') : 
        'No components found';

      const markdownResponse = `# Grasshopper Definition Overview

This is an **AI-generated summary** of your Grasshopper canvas.

## Canvas Statistics
- **Total Components**: ${componentCount}
- **Component List**: ${componentNames}

## Technical Description
The definition appears to be a ${componentCount > 5 ? 'complex' : 'simple'} setup. Data flows from source components (like sliders or panels) through various processing components to produce a final output.

## Canvas Data
\`\`\`json
${JSON.stringify(canvasData, null, 2)}
\`\`\`

*This overview is generated by the MCP server for Grasshopper integration.*`;

      return {
        content: [
          {
            type: "text",
            text: markdownResponse,
          },
        ],
      };
    } catch (error) {
      throw new McpError(ErrorCode.InternalError, `Failed to generate AI overview: ${error.message}`);
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