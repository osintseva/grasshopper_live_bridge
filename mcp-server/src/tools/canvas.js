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
function parseEnhancedPipeDelimitedLine(line) {
  // Skip comments and empty lines
  if (line.trim().startsWith('#') || line.trim() === '') {
    return null;
  }

  // Match the enhanced pipe-delimited format with more flexible bracket handling
  // We need to handle nested brackets in type names like List[Curve]
  const match = line.match(/^(\w+)\|(\d+,\d+)\|(\w+):\s*(.+?)\s*=\s*"([^"]+)"\s*\|\s*(\[.*?\])\s*\|\s*(\[.*?\])(?:\s*#\s*(.+))?$/);

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
 * Parse a parameter section like ["Input Name"(InputType):param_uuid, "_Unused Input"(Type):param_uuid]
 */
function parseParameterSection(section) {
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
    const paramMatch = part.trim().match(/"([^"]+)"\(([^)]+)\):(\w+)/);
    if (paramMatch) {
      const [, name, type, uuid] = paramMatch;
      parameters.push({
        name: name,
        type: type,
        uuid: uuid,
        isUnused: name.startsWith('_')
      });
    }
  }

  return parameters;
}

export async function getCanvasState(args = {}) {
  const { includeSelection = false, forceRefresh = false } = args;

  logger.toolCall('getCanvasState', args);

  try {
    const cache = getCanvasCache();
    const pseudocode = await cache.getCanvas(forceRefresh);

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
    const selection = await cache.getSelection(forceRefresh);

    // Get pseudocode for context
    const pseudocode = await cache.getCanvas();

    // Extract component info for selected items from pseudocode
    const selectedComponents = [];
    if (selection && Array.isArray(selection)) {
      for (const selectedId of selection) {
        const componentInfo = await cache.getComponentInfo(selectedId);
        if (componentInfo) {
          selectedComponents.push(componentInfo);
        }
      }
    }

    return {
      selectedIds: selection || [],
      selectedComponents,
      count: (selection || []).length,
      pseudocodeContext: pseudocode
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
    const lines = pseudocode.split('\n');

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
    const lines = pseudocode.split('\n');
    const fullUuidNoHyphens = componentUuid.replace(/-/g, '');
    const shortUuid = fullUuidNoHyphens.substring(0, 8);

    let componentLine = null;
    let parsedComponent = null;

    // Try to find the component using the new format
    for (const line of lines) {
      const parsed = parseEnhancedPipeDelimitedLine(line);
      if (parsed && (parsed.compUuid === shortUuid || parsed.compUuid === fullUuidNoHyphens)) {
        componentLine = line;
        parsedComponent = parsed;
        break;
      }
    }

    // Fallback to old string matching for backwards compatibility
    if (!componentLine) {
      componentLine = lines.find(line => line.includes(fullUuidNoHyphens));
      if (!componentLine) {
        componentLine = lines.find(line => line.includes(shortUuid));
      }
    }

    return {
      found: true,
      component,
      componentUuid,
      pseudocodeLine: componentLine,
      parsedComponent,
      pseudocodeContext: pseudocode
    };
  } catch (error) {
    logger.error('Failed to get component info', error);
    throw error;
  }
}


export async function findComponents(args = {}) {
  const { name, type, hasErrors = null } = args;

  logger.toolCall('findComponents', args);

  try {
    const cache = getCanvasCache();
    const pseudocode = await cache.getCanvas();

    const lines = pseudocode.split('\n');
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

    return {
      found: results.length,
      components: results,
      query: { name, type, hasErrors },
      note: hasErrors !== null ? 'Error filtering not available in pseudocode format' : null
    };
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
    connections = [],
    nickname = null
  } = args;

  logger.toolCall('createScriptComponent', args);

  try {
    const client = getGrasshopperClient();

    // Validate connections before creating component
    if (connections && connections.length > 0) {
      logger.info(`Validating ${connections.length} connections before component creation`);

      const cache = getCanvasCache();
      const pseudocode = await cache.getCanvas();
      const lines = pseudocode.split('\n');

      for (const connection of connections) {
        const { sourceId, sourceOutput = 0, targetInput = 0 } = connection;

        if (!sourceId) {
          logger.warn('Skipping connection with empty sourceId');
          continue;
        }

        // Try to find the source component in current canvas
        let sourceFound = false;

        // Check if sourceId is a full UUID
        if (sourceId.length === 36 || sourceId.length === 32) {
          const uuidToCheck = sourceId.replace(/-/g, '');
          sourceFound = lines.some(line => line.includes(uuidToCheck));
        }

        // Check if sourceId is a nickname by looking for it in component lines
        if (!sourceFound) {
          sourceFound = lines.some(line => {
            // Look for patterns like "nickname_uuid:" in component definitions
            return line.includes(':') && line.includes('=') &&
                   (line.toLowerCase().includes(sourceId.toLowerCase()) ||
                    line.includes(`${sourceId}_`));
          });
        }

        if (!sourceFound) {
          logger.warn(`Warning: Source component '${sourceId}' not found in current canvas. Connection may fail.`);
          logger.info(`Available components in canvas: ${lines.filter(line => line.includes(':') && line.includes('=')).length}`);
        } else {
          logger.info(`✓ Source component '${sourceId}' found for connection`);
        }
      }
    }

    const payload = {
      x,
      y,
      code,
      inputs,
      outputs,
      connections
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
        connectionCount: componentData.connectionCount || connections.length,
        message: 'Python component created successfully',
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