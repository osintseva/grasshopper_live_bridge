import { getCanvasCache } from '../state/canvas-cache.js';
import { getStore } from '../state/store.js';
import { getLogger } from '../utils/logger.js';
import { queryJson, filterJson, extractFields } from '../utils/json-query.js';
import { getGrasshopperClient } from '../websocket-client.js';

const logger = getLogger();
const store = getStore();

/**
 * Canvas inspection tools for MCP and HTTP
 */

/**
 * Parse a line in the new Enhanced Pipe-Delimited with Types format
 * Format: variable|x,y|comp_uuid: ComponentType = "Component Name" | ["Input Name"(InputType):param_uuid] | ["Output Name"(OutputType):param_uuid]
 */
export function parseEnhancedPipeDelimitedLine(line) {
  // Skip comments and empty lines
  if (line.trim().startsWith('#') || line.trim() === '') {
    return null;
  }

  // Clean line endings (Windows \r\n and Unix \n)
  const cleanLine = line.replace(/\r?\n$/, '').trim();

  // Match the enhanced pipe-delimited format with more flexible bracket handling
  // We need to handle nested brackets in type names like List[Curve]
  // Fixed regex to handle negative coordinates: -?\d+ instead of \d+
  // Updated to handle standard hyphenated GUIDs (36 characters): xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
  const match = cleanLine.match(/^(\w+)\|(-?\d+,-?\d+)\|([a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}):\s*(.+?)\s*=\s*"([^"]+)"\s*\|\s*(\[.*?\])\s*\|\s*(\[.*?\])(?:\s*#\s*(.+))?$/);

  if (!match) {
    return null;
  }

  const [, variable, position, compUuid, componentType, componentName, inputSection, outputSection, dataPreview] = match;

  // Parse position
  const [x, y] = position.split(',').map(n => parseInt(n));

  // Parse inputs
  const inputs = parseParameterSection(inputSection);

  // Parse outputs
  const outputs = parseParameterSection(outputSection);

  return {
    variable,
    position: { x, y },
    compUuid,
    componentType: componentType.trim(),
    componentName,
    inputs,
    outputs,
    dataPreview: dataPreview?.trim(),
    rawLine: line.trim()
  };
}

/**
 * Parse a parameter section like ["Input Name"(InputType):param_uuid, "Input Name"(Type):param_uuid]
 */
export function parseParameterSection(section) {
  if (!section || section === '[]') {
    return [];
  }

  // Remove the brackets
  const content = section.slice(1, -1);
  if (!content.trim()) {
    return [];
  }

  const parameters = [];
  // Split by comma, but be careful of commas inside quoted strings
  const parts = content.split(/,(?=\s*")/);

  for (const part of parts) {
    // Updated regex to handle standard hyphenated GUIDs (36 characters) and optional data preview
    // Format: "Name"(Type):uuid or "Name"(Type):uuid="preview with possible \" escapes"
    const paramMatch = part.trim().match(/"([^"]+)"\(([^)]+)\):([a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})(?:="((?:[^"\\]|\\.)*)")?/);
    if (paramMatch) {
      const [, name, type, uuid, dataPreview] = paramMatch;
      parameters.push({
        name: name,
        type: type,
        uuid: uuid,
        isUnused: name.startsWith('_'),
        dataPreview: dataPreview || undefined  // Only include if present
      });
    }
  }

  return parameters;
}

export async function getCanvasState(args = {}) {
  const { includeSelection = false, forceRefresh = false, includeDataPreviews = false, maxPreviewLength = 20 } = args;

  logger.toolCall('getCanvasState', args);

  try {
    const cache = getCanvasCache();
    const pseudocode = await cache.getCanvas(forceRefresh, includeDataPreviews, maxPreviewLength);

    return {
      type: 'pseudocode',
      content: pseudocode,
      timestamp: new Date().toISOString()
    };
  } catch (error) {
    logger.error('Failed to get canvas state', error);
    throw error;
  }
}

export async function getSelection(args = {}) {
  const { forceRefresh = false } = args;

  logger.toolCall('getSelection', args);

  try {
    const cache = getCanvasCache();
    const selectionResponse = await cache.getSelection(forceRefresh);

    // Get pseudocode only for parsing selected components (not for return)
    const pseudocode = await cache.getCanvas();

    // Parse selected components with detailed information
    const selectedComponents = [];
    let inputsConnected = 0;
    let outputsConnected = 0;

    // Extract the actual selection array from the response data
    const selection = selectionResponse?.selectedIds || selectionResponse?.data?.selectedIds || selectionResponse;
    const selectionCount = selectionResponse?.count || (Array.isArray(selection) ? selection.length : 0);

    if (selection && Array.isArray(selection)) {
      // Split pseudocode into lines for parsing
      const lines = pseudocode.split(/\r?\n/);

      for (const selectedId of selection) {
        // Find component in pseudocode - use standard hyphenated UUID (36 characters)
        const normalizedUuid = selectedId.toLowerCase();

        let componentLine = null;
        let parsedComponent = null;

        // Find the component using the new format
        for (const line of lines) {
          const parsed = parseEnhancedPipeDelimitedLine(line);
          if (parsed && parsed.compUuid.toLowerCase() === normalizedUuid) {
            componentLine = line;
            parsedComponent = parsed;
            break;
          }
        }

        if (parsedComponent) {
          // Count connections (simplified heuristic - connected if name doesn't start with _)
          const connectedInputs = parsedComponent.inputs.filter(inp => !inp.isUnused).length;
          const connectedOutputs = parsedComponent.outputs.filter(out => !out.isUnused).length;

          inputsConnected += connectedInputs;
          outputsConnected += connectedOutputs;

          selectedComponents.push({
            uuid: parsedComponent.compUuid,
            variable: parsedComponent.variable,
            type: parsedComponent.componentType,
            name: parsedComponent.componentName,
            position: parsedComponent.position,
            inputs: parsedComponent.inputs,
            outputs: parsedComponent.outputs,
            pseudocodeLine: componentLine.trim(),
            hasErrors: false, // TODO: Add error detection if available
            dataPreview: parsedComponent.dataPreview
          });
        } else {
          // Fallback for components not found in new format
          logger.warn(`Component ${selectedId} not found in pseudocode format`);
        }
      }
    }

    return {
      selectionSummary: {
        count: selectionCount,
        timestamp: new Date().toISOString()
      },
      selectedComponents,
      connectionSummary: {
        totalConnections: inputsConnected + outputsConnected,
        inputsConnected,
        outputsConnected
      }
    };
  } catch (error) {
    logger.error('Failed to get selection', error);
    throw error;
  }
}

export async function queryCanvasPseudocode(args = {}) {
  const { query, source = 'canvas', filePath = null } = args;

  logger.toolCall('queryCanvasPseudocode', args);

  try {
    let pseudocode;

    if (source === 'file' && filePath) {
      // Read from file
      const { readFile } = await import('../utils/file-system.js');
      pseudocode = await readFile(filePath);
      if (!pseudocode) {
        throw new Error(`Failed to read file: ${filePath}`);
      }
    } else {
      // Get from canvas
      const cache = getCanvasCache();
      pseudocode = await cache.getCanvas();
    }

    // Apply text-based query using regex or string search
    let result = [];
    // Split by both Unix (\n) and Windows (\r\n) line endings
    const lines = pseudocode.split(/\r?\n/);

    if (query.startsWith('/') && query.endsWith('/')) {
      // Regex query
      const regex = new RegExp(query.slice(1, -1), 'gi');
      result = lines.filter(line => regex.test(line));
    } else if (query.includes('*')) {
      // Wildcard query
      const regexPattern = query.replace(/\*/g, '.*');
      const regex = new RegExp(regexPattern, 'gi');
      result = lines.filter(line => regex.test(line));
    } else {
      // Simple text search
      result = lines.filter(line => line.toLowerCase().includes(query.toLowerCase()));
    }

    return {
      query,
      source,
      result,
      resultType: 'lines',
      resultCount: result.length,
      totalLines: lines.length
    };
  } catch (error) {
    logger.error('Failed to query canvas pseudocode', error);
    throw error;
  }
}

export async function getComponentInfo(args = {}) {
  const { componentUuid } = args;

  if (!componentUuid) {
    throw new Error('componentUuid is required');
  }

  logger.toolCall('getComponentInfo', args);

  try {
    const cache = getCanvasCache();
    const component = await cache.getComponentInfo(componentUuid);

    if (!component) {
      return {
        found: false,
        componentUuid
      };
    }

    // Get pseudocode for additional context
    const pseudocode = await cache.getCanvas();

    // Find the component's line in pseudocode for more details
    // Try to parse with the new Enhanced Pipe-Delimited format first
    // Split by both Unix (\n) and Windows (\r\n) line endings
    const lines = pseudocode.split(/\r?\n/);
    const normalizedUuid = componentUuid.toLowerCase();

    let componentLine = null;
    let parsedComponent = null;

    // Try to find the component using the new format (standard hyphenated UUID)
    for (const line of lines) {
      const parsed = parseEnhancedPipeDelimitedLine(line);
      if (parsed && parsed.compUuid.toLowerCase() === normalizedUuid) {
        componentLine = line;
        parsedComponent = parsed;
        break;
      }
    }

    // Fallback to old string matching for backwards compatibility
    if (!componentLine) {
      componentLine = lines.find(line => line.toLowerCase().includes(normalizedUuid));
    }

    return {
      found: true,
      component,
      componentUuid,
      pseudocodeLine: componentLine,
      parsedComponent
    };
  } catch (error) {
    logger.error('Failed to get component info', error);
    throw error;
  }
}


export async function findComponents(args = {}) {
  const { name, type, hasErrors = null, limit = 50, offset = 0 } = args;

  logger.toolCall('findComponents', args);

  try {
    const cache = getCanvasCache();
    const pseudocode = await cache.getCanvas();

    // Split by both Unix (\n) and Windows (\r\n) line endings
    const lines = pseudocode.split(/\r?\n/);
    let results = [];

    for (const line of lines) {
      // Try to parse with the new Enhanced Pipe-Delimited format
      const parsed = parseEnhancedPipeDelimitedLine(line);

      if (parsed) {
        let include = true;

        // Filter by name (check variable name and component name)
        if (name) {
          const regex = new RegExp(name, 'i');
          include = include && (regex.test(parsed.variable) || regex.test(parsed.componentName));
        }

        // Filter by type
        if (type) {
          include = include && parsed.componentType.toLowerCase().includes(type.toLowerCase());
        }

        // Note: Error filtering not available in pseudocode format
        if (hasErrors !== null) {
          // Skip error filtering for pseudocode - we don't have error information
        }

        if (include) {
          results.push({
            variable: parsed.variable,
            componentType: parsed.componentType,
            componentName: parsed.componentName,
            compUuid: parsed.compUuid,
            position: parsed.position,
            inputs: parsed.inputs,
            outputs: parsed.outputs,
            pseudocodeLine: line.trim(),
            // Legacy fields for backwards compatibility
            variableName: parsed.variable,
            typeName: parsed.componentType,
            functionName: parsed.componentName
          });
        }
      } else {
        // Fallback to old format parsing for backwards compatibility
        const match = line.match(/(\w+_[a-f0-9]+):\s*([\w\[\],]+)\s*=\s*([^(]+)\(/);
        if (match) {
          const [, variableName, typeName, functionName] = match;

          let include = true;

          // Filter by name (check variable name and function name)
          if (name) {
            const regex = new RegExp(name, 'i');
            include = include && (regex.test(variableName) || regex.test(functionName));
          }

          // Filter by type
          if (type) {
            include = include && functionName.toLowerCase().includes(type.toLowerCase());
          }

          if (include) {
            results.push({
              variableName,
              typeName,
              functionName,
              pseudocodeLine: line.trim()
            });
          }
        }
      }
    }

    // Apply pagination
    const totalCount = results.length;
    const paginatedResults = results.slice(offset, offset + limit);
    const hasMore = offset + limit < totalCount;

    // Create response with pagination info
    const response = {
      found: totalCount,
      returned: paginatedResults.length,
      components: paginatedResults,
      pagination: {
        limit,
        offset,
        hasMore,
        nextOffset: hasMore ? offset + limit : null
      },
      query: { name, type, hasErrors },
      note: hasErrors !== null ? 'Error filtering not available in pseudocode format' : null
    };

    // Add warning for large result sets
    if (totalCount > 100) {
      response.warning = `Large result set (${totalCount} components). Use pagination with limit/offset parameters to browse all results.`;
    }

    return response;
  } catch (error) {
    logger.error('Failed to find components', error);
    throw error;
  }
}


export async function createScriptComponent(args = {}) {
  const {
    x = 200,
    y = 300,
    code = '',
    inputs = [],
    outputs = [],
    nickname = null
  } = args;

  logger.toolCall('createScriptComponent', args);

  try {
    const client = getGrasshopperClient();

    const payload = {
      x,
      y,
      code,
      inputs,
      outputs
    };

    if (nickname) {
      payload.nickname = nickname;
    }

    logger.info('Sending create_python_component request to Grasshopper');
    const response = await client.send('create_python_component', payload);

    if (response && response.status === 'success') {
      logger.info('Python component created successfully');

      // Force refresh canvas cache since we've modified it
      const cache = getCanvasCache();
      await cache.getCanvas(true);

      // Extract component information from response
      const componentData = response.data || {};
      const componentId = componentData.componentId || componentData.componentUuid || response.componentId;

      return {
        success: true,
        componentId,
        componentUuid: componentData.componentUuid || componentId,
        nickname: componentData.nickname || nickname,
        inputCount: componentData.inputCount || inputs.length,
        outputCount: componentData.outputCount || outputs.length,
        message: 'Python component created successfully. Use manage_wire_connections to connect inputs.',
        response: response.data || response
      };
    } else {
      logger.error('Failed to create Python component', response);
      return {
        success: false,
        error: response?.error || response?.message || 'Unknown error',
        response
      };
    }
  } catch (error) {
    logger.error('Failed to create Python component', error);
    throw error;
  }
}

export async function manageWireConnections(args = {}) {
  const {
    action = 'connect',
    connections = [],
    partialOperations = []
  } = args;

  logger.toolCall('manageWireConnections', args);

  try {
    const client = getGrasshopperClient();

    const response = await client.send('manage_wires', {
      action,
      connections,
      partialOperations
    });

    if (response.status === 'success') {
      const data = response.data || {};
      return {
        success: true,
        action,
        successCount: data.successCount || 0,
        failureCount: data.failureCount || 0,
        errors: data.errors || []
      };
    } else {
      throw new Error(response.message || 'Wire management failed');
    }
  } catch (error) {
    logger.error('Failed to manage wire connections', error);
    throw error;
  }
}