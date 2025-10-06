import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ErrorCode,
  ListToolsRequestSchema,
  ListPromptsRequestSchema,
  GetPromptRequestSchema,
  McpError,
} from "@modelcontextprotocol/sdk/types.js";

import { getStore } from '../state/store.js';
import { getLogger } from '../utils/logger.js';
import * as canvasTools from '../tools/canvas.js';
import * as prompts from '../prompts/index.js';

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
        prompts: {},
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
              },
              includeDataPreviews: {
                type: "boolean",
                description: "Include data preview values inline in output parameters",
                default: false
              },
              maxPreviewLength: {
                type: "number",
                description: "Maximum character length for each data preview (default 20)",
                default: 20
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
          name: "query_canvas_pseudocode",
          description: "Query canvas pseudocode using text search, regex, or wildcards",
          inputSchema: {
            type: "object",
            properties: {
              query: {
                type: "string",
                description: "Search query (text, regex /pattern/, or wildcard with *)"
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
              },
              limit: {
                type: "number",
                description: "Maximum number of results to return",
                default: 50
              },
              offset: {
                type: "number",
                description: "Number of results to skip (for pagination)",
                default: 0
              }
            },
            additionalProperties: false,
          },
        },
        {
          name: "create_script_component",
          description: "Create a Python component on the Grasshopper canvas with custom inputs/outputs. WORKFLOW: (1) Create component, (2) Use manage_wire_connections to connect inputs (do this automatically unless user specifies otherwise), (3) ALWAYS use get_component_info to check for runtime errors/warnings, (4) If errors found, fix the code and recreate. IMPORTANT: To output lists/trees properly, use DataTree: `import Grasshopper as gh` then `output_var = gh.DataTree[object]()` and `output_var.AddRange(list_data, gh.Kernel.Data.GH_Path(0))`. Single values can be assigned directly.",
          inputSchema: {
            type: "object",
            properties: {
              x: {
                type: "number",
                description: "X coordinate on canvas",
                default: 200
              },
              y: {
                type: "number",
                description: "Y coordinate on canvas",
                default: 300
              },
              code: {
                type: "string",
                description: "Python code for the component"
              },
              inputs: {
                type: "array",
                description: "Input parameter definitions",
                items: {
                  type: "object",
                  properties: {
                    name: {
                      type: "string",
                      description: "Input parameter name"
                    },
                    nickname: {
                      type: "string",
                      description: "Short display name"
                    },
                    optional: {
                      type: "boolean",
                      description: "Whether input is optional",
                      default: true
                    },
                    access: {
                      type: "string",
                      description: "Access type (item, list, tree)",
                      default: "item"
                    },
                    typeHint: {
                      type: "string",
                      description: "Type hint (double, number, string, etc.)"
                    }
                  },
                  required: ["name"]
                }
              },
              outputs: {
                type: "array",
                description: "Output parameter definitions",
                items: {
                  type: "object",
                  properties: {
                    name: {
                      type: "string",
                      description: "Output parameter name"
                    },
                    nickname: {
                      type: "string",
                      description: "Short display name"
                    }
                  },
                  required: ["name"]
                }
              },
              nickname: {
                type: "string",
                description: "Component nickname"
              }
            },
            required: ["code"],
            additionalProperties: false,
          },
        },
        {
          name: "manage_wire_connections",
          description: "Connect or disconnect wires between Grasshopper components",
          inputSchema: {
            type: "object",
            properties: {
              action: {
                type: "string",
                enum: ["connect", "disconnect"],
                description: "Wire operation type",
                default: "connect"
              },
              connections: {
                type: "array",
                description: "Direct wire connections to create/remove",
                items: {
                  type: "object",
                  properties: {
                    sourceComponentUuid: {
                      type: "string",
                      description: "Source component UUID (36-char hyphenated)"
                    },
                    sourceOutputIndex: {
                      type: "number",
                      description: "Source output parameter index"
                    },
                    targetComponentUuid: {
                      type: "string",
                      description: "Target component UUID (36-char hyphenated)"
                    },
                    targetInputIndex: {
                      type: "number",
                      description: "Target input parameter index"
                    }
                  },
                  required: ["sourceComponentUuid", "sourceOutputIndex", "targetComponentUuid", "targetInputIndex"]
                }
              },
              partialOperations: {
                type: "array",
                description: "Operations on specific parameters (e.g., disconnect all)",
                items: {
                  type: "object",
                  properties: {
                    componentUuid: {
                      type: "string",
                      description: "Component UUID"
                    },
                    parameterType: {
                      type: "string",
                      enum: ["input", "output"],
                      description: "Parameter type"
                    },
                    parameterIndex: {
                      type: "number",
                      description: "Parameter index"
                    },
                    operation: {
                      type: "string",
                      enum: ["disconnect_all"],
                      description: "Operation to perform"
                    }
                  },
                  required: ["componentUuid", "parameterType", "parameterIndex", "operation"]
                }
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
        
        case "query_canvas_pseudocode":
          result = await canvasTools.queryCanvasPseudocode(args || {});
          break;

        
        case "get_component_info":
          result = await canvasTools.getComponentInfo(args || {});
          break;
        
        
        case "find_components":
          result = await canvasTools.findComponents(args || {});
          break;

        case "create_script_component":
          result = await canvasTools.createScriptComponent(args || {});
          break;

        case "manage_wire_connections":
          result = await canvasTools.manageWireConnections(args || {});
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

  // Handle prompt list requests
  server.setRequestHandler(ListPromptsRequestSchema, async () => {
    const availablePrompts = await prompts.getAvailablePrompts();
    return {
      prompts: availablePrompts
    };
  });

  // Handle get prompt requests
  server.setRequestHandler(GetPromptRequestSchema, async (request) => {
    try {
      const { name, arguments: args } = request.params;
      const result = await prompts.getPrompt(name, args || {});
      
      return result;
    } catch (error) {
      logger.error(`Prompt error: ${error.message}`, error);
      throw new McpError(
        ErrorCode.InternalError,
        `Prompt execution failed: ${error.message}`
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