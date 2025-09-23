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
          name: "analyze_pseudocode",
          description: "Analyze the canvas pseudocode and prepare it for technical specification generation",
          inputSchema: {
            type: "object",
            properties: {
              includePrompt: {
                type: "boolean",
                description: "Include the analysis prompt in the response",
                default: false
              }
            },
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
        {
          name: "create_script_component",
          description: "Create a Python component on the Grasshopper canvas with custom inputs/outputs and connections",
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
              connections: {
                type: "array",
                description: "Automatic connections to create",
                items: {
                  type: "object",
                  properties: {
                    sourceId: {
                      type: "string",
                      description: "Source component ID or nickname"
                    },
                    sourceOutput: {
                      type: "number",
                      description: "Source output index"
                    },
                    targetInput: {
                      type: "number",
                      description: "Target input index"
                    }
                  },
                  required: ["sourceId", "sourceOutput", "targetInput"]
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

        case "analyze_pseudocode":
          result = await canvasTools.analyzePseudocode(args || {});
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

        case "create_script_component":
          result = await canvasTools.createScriptComponent(args || {});
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