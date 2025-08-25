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
    const canvasData = await cache.getCanvas(forceRefresh);
    
    // Filter out selection if not requested
    if (!includeSelection && canvasData.Selection) {
      const filtered = { ...canvasData };
      delete filtered.Selection;
      return filtered;
    }
    
    return canvasData;
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
    
    // Also get component details for selected items
    if (selection && Array.isArray(selection)) {
      const canvas = await cache.getCanvas();
      const selectedComponents = canvas.Components?.filter(c => 
        selection.includes(c.InstanceGuid)
      ) || [];
      
      return {
        selectedIds: selection,
        selectedComponents,
        count: selection.length
      };
    }
    
    return {
      selectedIds: [],
      selectedComponents: [],
      count: 0
    };
  } catch (error) {
    logger.error('Failed to get selection', error);
    throw error;
  }
}

export async function queryCanvasJson(args = {}) {
  const { query, source = 'canvas', filePath = null } = args;
  
  logger.toolCall('queryCanvasJson', args);
  
  try {
    let data;
    
    if (source === 'file' && filePath) {
      // Read from file
      const { readFile } = await import('../utils/file-system.js');
      const content = await readFile(filePath);
      if (!content) {
        throw new Error(`Failed to read file: ${filePath}`);
      }
      data = JSON.parse(content);
    } else {
      // Get from canvas
      const cache = getCanvasCache();
      data = await cache.getCanvas();
    }
    
    // Apply query
    const result = queryJson(data, query);
    
    return {
      query,
      source,
      result,
      resultType: Array.isArray(result) ? 'array' : typeof result,
      resultCount: Array.isArray(result) ? result.length : null
    };
  } catch (error) {
    logger.error('Failed to query canvas JSON', error);
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
    
    // Get connections for this component
    const canvas = await cache.getCanvas();
    const connections = canvas.Connections?.filter(conn => 
      conn.SourceId === componentUuid || conn.TargetId === componentUuid
    ) || [];
    
    return {
      found: true,
      component,
      connections,
      inputCount: component.Inputs?.length || 0,
      outputCount: component.Outputs?.length || 0
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
    const canvas = await cache.getCanvas();
    
    // Count components by type
    const componentTypes = {};
    for (const comp of (canvas.Components || [])) {
      const type = comp.Name || 'Unknown';
      componentTypes[type] = (componentTypes[type] || 0) + 1;
    }
    
    // Analyze connections
    const connectionStats = {
      total: canvas.Connections?.length || 0,
      bySourceType: {},
      byTargetType: {}
    };
    
    for (const conn of (canvas.Connections || [])) {
      const sourceComp = canvas.Components?.find(c => c.InstanceGuid === conn.SourceId);
      const targetComp = canvas.Components?.find(c => c.InstanceGuid === conn.TargetId);
      
      if (sourceComp) {
        const type = sourceComp.Name || 'Unknown';
        connectionStats.bySourceType[type] = (connectionStats.bySourceType[type] || 0) + 1;
      }
      
      if (targetComp) {
        const type = targetComp.Name || 'Unknown';
        connectionStats.byTargetType[type] = (connectionStats.byTargetType[type] || 0) + 1;
      }
    }
    
    return {
      componentCount: canvas.Components?.length || 0,
      componentTypes,
      connectionStats,
      hasErrors: canvas.Components?.some(c => c.RuntimeMessages?.length > 0) || false,
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
    const canvas = await cache.getCanvas();
    
    let components = canvas.Components || [];
    
    // Filter by name
    if (name) {
      const regex = new RegExp(name, 'i');
      components = components.filter(c => 
        regex.test(c.NickName) || regex.test(c.Name)
      );
    }
    
    // Filter by type
    if (type) {
      components = components.filter(c => c.Name === type);
    }
    
    // Filter by error status
    if (hasErrors !== null) {
      components = components.filter(c => {
        const hasErr = (c.RuntimeMessages?.length || 0) > 0;
        return hasErrors ? hasErr : !hasErr;
      });
    }
    
    return {
      found: components.length,
      components: extractFields(components, ['InstanceGuid', 'Name', 'NickName']),
      query: { name, type, hasErrors }
    };
  } catch (error) {
    logger.error('Failed to find components', error);
    throw error;
  }
}