import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ErrorCode,
  ListToolsRequestSchema,
  McpError,
} from "@modelcontextprotocol/sdk/types.js";

import { getStore } from '../state/store.js';
import { getLogger } from '../utils/logger.js';
import * as canvasTools from '../tools/canvas.js';
import * as scriptTools from '../tools/scripts.js';

const logger = getLogger();

export function createMcpServer(config = {}) {
  const store = getStore();
  
  const server = new Server(
    {
      name: "grasshopper-mcp",
      version: "1.0.0",
    },
    {
      capabilities: {
        tools: {},
      },
    }
  );

  // List available tools
  server.setRequestHandler(ListToolsRequestSchema, async () => {
    return {
      tools: [
        // Canvas tools
        {
          name: "get_canvas_state",
          description: "Get the current state of the Grasshopper canvas",
          inputSchema: {
            type: "object",
            properties: {
              includeSelection: {
                type: "boolean",
                description: "Include selection information",
                default: false
              },
              forceRefresh: {
                type: "boolean",
                description: "Force refresh from Grasshopper",
                default: false
              }
            },
            additionalProperties: false,
          },
        },
        {
          name: "get_selection",
          description: "Get the current selection in Grasshopper with component details",
          inputSchema: {
            type: "object",
            properties: {
              forceRefresh: {
                type: "boolean",
                description: "Force refresh from Grasshopper",
                default: false
              }
            },
            additionalProperties: false,
          },
        },
        {
          name: "query_canvas_json",
          description: "Query canvas JSON data using JSONPath-like expressions",
          inputSchema: {
            type: "object",
            properties: {
              query: {
                type: "string",
                description: "JSONPath query (e.g., '$.Components[0].Name')"
              },
              source: {
                type: "string",
                enum: ["canvas", "file"],
                description: "Data source",
                default: "canvas"
              },
              filePath: {
                type: "string",
                description: "File path when source is 'file'"
              }
            },
            required: ["query"],
            additionalProperties: false,
          },
        },
        {
          name: "get_component_info",
          description: "Get detailed information about a specific component",
          inputSchema: {
            type: "object",
            properties: {
              componentUuid: {
                type: "string",
                description: "The UUID of the component"
              }
            },
            required: ["componentUuid"],
            additionalProperties: false,
          },
        },
        {
          name: "get_canvas_statistics",
          description: "Get statistics about the current canvas",
          inputSchema: {
            type: "object",
            properties: {},
            additionalProperties: false,
          },
        },
        {
          name: "find_components",
          description: "Find components by name, type, or error status",
          inputSchema: {
            type: "object",
            properties: {
              name: {
                type: "string",
                description: "Component name or nickname (regex)"
              },
              type: {
                type: "string",
                description: "Component type"
              },
              hasErrors: {
                type: "boolean",
                description: "Filter by error status"
              }
            },
            additionalProperties: false,
          },
        },
        
        // Script tools
        {
          name: "create_script_file",
          description: "Create a new script file for a Grasshopper component",
          inputSchema: {
            type: "object",
            properties: {
              componentUuid: {
                type: "string",
                description: "The UUID of the component"
              },
              language: {
                type: "string",
                enum: ["python", "cs", "vb"],
                description: "Script language",
                default: "python"
              },
              nameHint: {
                type: "string",
                description: "Suggested name for the script"
              }
            },
            required: ["componentUuid"],
            additionalProperties: false,
          },
        },
        {
          name: "push_script_update",
          description: "Push script changes to Grasshopper",
          inputSchema: {
            type: "object",
            properties: {
              componentUuid: {
                type: "string",
                description: "The UUID of the component"
              },
              filePath: {
                type: "string",
                description: "Path to the script file"
              },
              language: {
                type: "string",
                enum: ["python", "cs", "vb"],
                description: "Script language"
              }
            },
            required: ["componentUuid"],
            additionalProperties: false,
          },
        },
        {
          name: "list_scripts",
          description: "List all script files in the project",
          inputSchema: {
            type: "object",
            properties: {
              includeContent: {
                type: "boolean",
                description: "Include file content",
                default: false
              }
            },
            additionalProperties: false,
          },
        },
        {
          name: "confirm_last_update",
          description: "Confirm the last script update was applied",
          inputSchema: {
            type: "object",
            properties: {
              componentUuid: {
                type: "string",
                description: "The UUID of the component"
              },
              sha256: {
                type: "string",
                description: "Expected SHA256 hash"
              }
            },
            required: ["componentUuid"],
            additionalProperties: false,
          },
        },
        {
          name: "delete_script_file",
          description: "Delete a script file",
          inputSchema: {
            type: "object",
            properties: {
              componentUuid: {
                type: "string",
                description: "The UUID of the component"
              },
              filePath: {
                type: "string",
                description: "Path to the script file"
              }
            },
            additionalProperties: false,
          },
        },
        
        // Testing tools
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
        {
          name: "get_recent_logs",
          description: "Get recent event logs",
          inputSchema: {
            type: "object",
            properties: {
              limit: {
                type: "number",
                description: "Number of logs to retrieve",
                default: 20
              },
              kind: {
                type: "string",
                description: "Filter by event kind"
              }
            },
            additionalProperties: false,
          },
        },
      ],
    };
  });

  // Handle tool calls
  server.setRequestHandler(CallToolRequestSchema, async (request) => {
    try {
      const { name, arguments: args } = request.params;
      logger.toolCall(name, args);
      
      let result;
      
      switch (name) {
        // Canvas tools
        case "get_canvas_state":
          result = await canvasTools.getCanvasState(args || {});
          break;
        
        case "get_selection":
          result = await canvasTools.getSelection(args || {});
          break;
        
        case "query_canvas_json":
          result = await canvasTools.queryCanvasJson(args || {});
          break;
        
        case "get_component_info":
          result = await canvasTools.getComponentInfo(args || {});
          break;
        
        case "get_canvas_statistics":
          result = await canvasTools.getCanvasStatistics(args || {});
          break;
        
        case "find_components":
          result = await canvasTools.findComponents(args || {});
          break;
        
        // Script tools
        case "create_script_file":
          result = await scriptTools.createScriptFile(args || {});
          break;
        
        case "push_script_update":
          result = await scriptTools.pushScriptUpdate(args || {});
          break;
        
        case "list_scripts":
          result = await scriptTools.listScripts(args || {});
          break;
        
        case "confirm_last_update":
          result = await scriptTools.confirmLastUpdate(args || {});
          break;
        
        case "delete_script_file":
          result = await scriptTools.deleteScriptFile(args || {});
          break;
        
        // Testing tools
        case "hello_world":
          result = { message: `Hello, ${args?.name || 'World'}!` };
          break;
        
        case "get_recent_logs":
          const limit = args?.limit || 20;
          const kind = args?.kind || null;
          const events = store.getEvents(0, kind).slice(0, limit);
          result = { events, count: events.length };
          break;
        
        default:
          throw new McpError(
            ErrorCode.MethodNotFound,
            `Unknown tool: ${name}`
          );
      }
      
      // Format result for MCP
      return {
        content: [
          {
            type: "text",
            text: typeof result === 'string' ? result : JSON.stringify(result, null, 2),
          },
        ],
      };
      
    } catch (error) {
      logger.error(`MCP tool error: ${error.message}`, error);
      throw new McpError(
        ErrorCode.InternalError,
        `Tool execution failed: ${error.message}`
      );
    }
  });

  async function run() {
    const transport = new StdioServerTransport();
    await server.connect(transport);
    logger.info("MCP server running on stdio");
  }

  return {
    server,
    run
  };
}

export default createMcpServer;