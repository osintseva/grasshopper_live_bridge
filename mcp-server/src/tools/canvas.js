import { getCanvasCache } from '../state/canvas-cache.js';
import { getStore } from '../state/store.js';
import { getLogger } from '../utils/logger.js';
import { queryJson, filterJson, extractFields } from '../utils/json-query.js';

const logger = getLogger();
const store = getStore();

/**
 * Canvas inspection tools for MCP and HTTP
 */

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
    const shortUuid = componentUuid.replace(/-/g, '').substring(0, 8);
    const lines = pseudocode.split('\n');
    const componentLine = lines.find(line => line.includes(shortUuid));

    return {
      found: true,
      component,
      componentUuid,
      pseudocodeLine: componentLine,
      pseudocodeContext: pseudocode
    };
  } catch (error) {
    logger.error('Failed to get component info', error);
    throw error;
  }
}

export async function getCanvasStatistics(args = {}) {
  logger.toolCall('getCanvasStatistics', args);

  try {
    const cache = getCanvasCache();
    const pseudocode = await cache.getCanvas();

    const lines = pseudocode.split('\n');

    // Extract statistics from pseudocode
    const headerLine = lines.find(line => line.includes('Components:'));
    let componentCount = 0;
    let connectionCount = 0;
    let sourceCount = 0;
    let sinkCount = 0;

    if (headerLine) {
      const match = headerLine.match(/Components:\s*(\d+)\s*\|\s*Connections:\s*(\d+)\s*\|\s*Sources:\s*(\d+)\s*\|\s*Sinks:\s*(\d+)/);
      if (match) {
        componentCount = parseInt(match[1]);
        connectionCount = parseInt(match[2]);
        sourceCount = parseInt(match[3]);
        sinkCount = parseInt(match[4]);
      }
    }

    // Count component types from function calls
    const componentTypes = {};
    const functionCallRegex = /(\w+):\s*[\w\[\],]+\s*=\s*([^(]+)\(/g;
    let match;

    while ((match = functionCallRegex.exec(pseudocode)) !== null) {
      const functionName = match[2].trim();
      componentTypes[functionName] = (componentTypes[functionName] || 0) + 1;
    }

    // Count sections
    const sections = {
      sources: lines.filter(line => line.includes('=== SOURCE DATA')).length > 0,
      processors: lines.filter(line => line.includes('=== MAIN PROCESSING CHAIN')).length > 0,
      sinks: lines.filter(line => line.includes('=== DISCONNECTED/DISPLAY')).length > 0
    };

    return {
      componentCount,
      connectionCount,
      sourceCount,
      sinkCount,
      componentTypes,
      sections,
      linesOfPseudocode: lines.length,
      timestamp: new Date().toISOString()
    };
  } catch (error) {
    logger.error('Failed to get canvas statistics', error);
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
    const componentLines = lines.filter(line => line.includes(':') && line.includes('='));

    let results = [];

    for (const line of componentLines) {
      // Extract component info from pseudocode line
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

        // Note: Error filtering not available in pseudocode format
        if (hasErrors !== null) {
          // Skip error filtering for pseudocode - we don't have error information
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

export async function analyzePseudocode(args = {}) {
  const { includePrompt = false } = args;

  logger.toolCall('analyzePseudocode', args);

  try {
    const cache = getCanvasCache();
    const pseudocode = await cache.getCanvas();

    // Read the analysis prompt from file
    const { readFile } = await import('../utils/file-system.js');
    const path = await import('path');
    const { fileURLToPath } = await import('url');
    const __filename = fileURLToPath(import.meta.url);
    const __dirname = path.dirname(__filename);
    const promptPath = path.join(__dirname, '..', '..', '..', 'misc', 'gh2json_converter', 'grasshopper_analysis_prompt.md');
    const analysisPrompt = await readFile(promptPath);

    if (!analysisPrompt) {
      throw new Error('Failed to read analysis prompt file');
    }

    // Prepare the analysis
    const analysis = {
      pseudocode,
      analysisPrompt: includePrompt ? analysisPrompt : null,
      instructions: 'Apply the analysis prompt to this pseudocode to generate a technical specification',
      timestamp: new Date().toISOString(),
      wordCount: pseudocode.split(/\s+/).length,
      lineCount: pseudocode.split('\n').length
    };

    // Extract basic algorithm info from pseudocode
    const lines = pseudocode.split('\n');
    const headerLine = lines.find(line => line.includes('Components:'));
    const sourcesSection = lines.find(line => line.includes('=== SOURCE DATA'));
    const processingSection = lines.find(line => line.includes('=== MAIN PROCESSING CHAIN'));
    const sinksSection = lines.find(line => line.includes('=== DISCONNECTED/DISPLAY'));

    analysis.structure = {
      hasSources: !!sourcesSection,
      hasProcessing: !!processingSection,
      hasSinks: !!sinksSection,
      headerInfo: headerLine
    };

    // Extract component references for analysis
    const componentPattern = /(\w+_[a-f0-9]+):/g;
    const componentReferences = [];
    let match;

    while ((match = componentPattern.exec(pseudocode)) !== null) {
      componentReferences.push(match[1]);
    }

    analysis.componentReferences = componentReferences;
    analysis.uniqueComponents = [...new Set(componentReferences)].length;

    return analysis;
  } catch (error) {
    logger.error('Failed to analyze pseudocode', error);
    throw error;
  }
}