# Development Guides

This document provides step-by-step guides for extending the Grasshopper MCP server with new tools, prompts, and resources.

## MCP Concepts Overview

The **Model Context Protocol (MCP)** enables AI assistants to securely connect to external systems. Our Grasshopper MCP server exposes three main types of capabilities:

### 🛠️ Tools
- **Purpose**: Execute actions and operations (e.g., get canvas data, create scripts)
- **Usage**: Called by AI assistants to perform tasks
- **Examples**: `get_canvas_state`, `find_components`, `push_script_update`

### 💭 Prompts  
- **Purpose**: Provide reusable, dynamic text templates for AI conversations
- **Usage**: AI assistants can request pre-built prompts with context
- **Examples**: `analyze_canvas`, `debug_component`

### 📄 Resources
- **Purpose**: Expose data that AI assistants can read (like files or API endpoints)
- **Usage**: AI assistants can browse and read available resources
- **Examples**: `gh://canvas/current`, `gh://scripts/my_script.py`

**Key Files:**
- **Tool definitions**: `src/servers/mcp.js` (lines 33-284)
- **Tool implementations**: `src/tools/canvas.js`, `src/tools/scripts.js` 
- **Sample prompts**: `src/prompts/index.js`
- **Sample resources**: `src/resources/index.js`

## Guide 1: Adding a New Tool

Tools are the main way to expose functionality to MCP clients. Follow these steps to add a new tool:

### Step 1: Create the Tool Implementation

Create or add your tool function to the appropriate file in `src/tools/`:

**For canvas-related tools**: Add to `src/tools/canvas.js`
**For script-related tools**: Add to `src/tools/scripts.js`
**For new categories**: Create a new file like `src/tools/your-category.js`

```javascript
// Example: src/tools/canvas.js
export async function getComponentsByLayer(args = {}) {
  const { layerName } = args;
  
  if (!layerName) {
    throw new Error('layerName is required');
  }
  
  logger.toolCall('getComponentsByLayer', args);
  
  try {
    const cache = getCanvasCache();
    const canvas = await cache.getCanvas();
    
    const components = canvas.Components?.filter(c => 
      c.Layer === layerName
    ) || [];
    
    return {
      layerName,
      components: components.map(c => ({
        uuid: c.InstanceGuid,
        name: c.Name,
        nickname: c.NickName
      })),
      count: components.length
    };
  } catch (error) {
    logger.error('Failed to get components by layer', error);
    throw error;
  }
}
```

### Step 2: Register the Tool in MCP Server

Add the tool definition to `src/servers/mcp.js` in two places:

**A. Add to the tools array in `ListToolsRequestSchema` handler:**

```javascript
// Around line 280, add to the tools array:
{
  name: "get_components_by_layer",
  description: "Get all components in a specific layer",
  inputSchema: {
    type: "object",
    properties: {
      layerName: {
        type: "string",
        description: "Name of the layer to search"
      }
    },
    required: ["layerName"],
    additionalProperties: false,
  },
}
```

**B. Add case handler in `CallToolRequestSchema`:**

```javascript
// Around line 318, add new case:
case "get_components_by_layer":
  result = await canvasTools.getComponentsByLayer(args || {});
  break;
```

### Step 3: Import New Tool Functions (if new file)

If you created a new tools file, import it at the top of `src/servers/mcp.js`:

```javascript
import * as yourCategoryTools from '../tools/your-category.js';
```

### Step 4: Test Your Tool

1. Restart the MCP server
2. Test using Claude Code MCP tools or HTTP endpoint
3. Check logs in `get_recent_logs` for debugging

## Guide 2: Adding a New Prompt

Prompts provide reusable, dynamic text templates. The server already supports prompts with sample implementations in `src/prompts/index.js`.

### Step 1: Add Your Prompt Function

Edit `src/prompts/index.js` and add your prompt to both `getAvailablePrompts()` and `getPrompt()`:

```javascript
// Add to getAvailablePrompts() return array:
{
  name: "your_prompt_name",
  description: "What your prompt does",
  arguments: [
    {
      name: "param_name",
      description: "Parameter description", 
      required: true
    }
  ]
}

// Add to getPrompt() switch statement:
case "your_prompt_name":
  return await generateYourPrompt(args);

// Add your prompt generator function:
async function generateYourPrompt(args) {
  // Use canvas data, args, etc. to build prompt
  const prompt = `Your dynamic prompt text here...`;
  
  return {
    description: "Prompt description",
    messages: [{
      role: "user",
      content: { type: "text", text: prompt }
    }]
  };
}
```

### Step 2: Test Your Prompt

That's it! The MCP server already has prompts enabled. Just:

1. Restart the MCP server
2. Your new prompt will be available to MCP clients
3. Check `get_recent_logs` for debugging

## Guide 3: Adding a New Resource

Resources expose data that clients can read. Sample implementation exists at `src/resources/index.js`.

### Step 1: Add Your Resource

Edit `src/resources/index.js` and add your resource to `getAvailableResources()` and handle it in `getResource()`:

```javascript
// Add to getAvailableResources() return array:
resources.push({
  uri: "gh://your/resource/uri",
  name: "Resource Name",
  description: "What this resource provides", 
  mimeType: "application/json" // or text/plain, etc.
});

// Add to getResource() URI routing:
else if (uri.startsWith('gh://your/')) {
  return await getYourResource(uri);
}

// Add your resource handler function:
async function getYourResource(uri) {
  // Generate or fetch your resource data
  const data = "your resource content";
  
  return {
    contents: [{
      uri,
      mimeType: "text/plain",
      text: data
    }]
  };
}
```

### Step 2: Enable Resources in MCP Server

Add the following to `src/servers/mcp.js`:

```javascript
import {
  ListResourcesRequestSchema,
  ReadResourceRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";
import * as resources from '../resources/index.js';

// Add to capabilities:
const server = new Server(
  {
    name: "grasshopper-mcp",
    version: "1.0.0",
  },
  {
    capabilities: {
      tools: {},
      prompts: {},
      resources: {}, // Add this line
    },
  }
);

// Add resource list handler:
server.setRequestHandler(ListResourcesRequestSchema, async () => {
  const availableResources = await resources.getAvailableResources();
  return {
    resources: availableResources
  };
});

// Add read resource handler:
server.setRequestHandler(ReadResourceRequestSchema, async (request) => {
  try {
    const { uri } = request.params;
    const result = await resources.getResource(uri);
    
    return result;
  } catch (error) {
    logger.error(`Resource error: ${error.message}`, error);
    throw new McpError(
      ErrorCode.InternalError,
      `Resource access failed: ${error.message}`
    );
  }
});
```

## Testing & Best Practices

### Testing Your Extensions
1. Restart the MCP server
2. Test with Claude Code MCP tools 
3. Check `get_recent_logs` for debugging
4. Validate JSON schemas

### Best Practices
- **Error Handling**: Always use try/catch
- **Logging**: Use `logger.toolCall()` to track usage
- **Validation**: Validate required parameters early
- **Documentation**: Update descriptions and schemas
- **URI Patterns**: Use consistent URI schemes (e.g., `gh://category/item`)