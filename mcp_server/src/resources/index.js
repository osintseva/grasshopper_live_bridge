import { getLogger } from '../utils/logger.js';
import { getCanvasCache } from '../state/canvas-cache.js';
import { getStore } from '../state/store.js';
import * as fs from '../utils/file-system.js';
import path from 'path';

const logger = getLogger();

export async function getAvailableResources() {
  const resources = [];
  
  // Canvas resources
  resources.push({
    uri: "gh://canvas/current",
    name: "Current Canvas",
    description: "Live Grasshopper canvas data",
    mimeType: "application/json"
  });
  
  resources.push({
    uri: "gh://canvas/components",
    name: "Components List", 
    description: "List of all components in the canvas",
    mimeType: "application/json"
  });
  
  // Script resources
  const scriptResources = await listScriptResources();
  resources.push(...scriptResources);
  
  // Log resources
  resources.push({
    uri: "gh://logs/recent",
    name: "Recent Logs",
    description: "Recent server event logs",
    mimeType: "application/json"
  });
  
  return resources;
}

export async function getResource(uri) {
  logger.toolCall('getResource', { uri });
  
  if (uri.startsWith('gh://canvas/')) {
    return await getCanvasResource(uri);
  } else if (uri.startsWith('gh://scripts/')) {
    return await getScriptResource(uri);
  } else if (uri.startsWith('gh://logs/')) {
    return await getLogResource(uri);
  } else {
    throw new Error(`Unknown resource URI: ${uri}`);
  }
}

async function getCanvasResource(uri) {
  const cache = getCanvasCache();
  
  switch (uri) {
    case 'gh://canvas/current':
      const canvas = await cache.getCanvas();
      return {
        contents: [
          {
            uri,
            mimeType: "application/json",
            text: JSON.stringify(canvas, null, 2)
          }
        ]
      };
      
    case 'gh://canvas/components':
      const canvasData = await cache.getCanvas();
      const components = canvasData.Components?.map(c => ({
        uuid: c.InstanceGuid,
        name: c.Name,
        nickname: c.NickName,
        type: c.TypeName,
        hasErrors: (c.RuntimeMessages?.length || 0) > 0,
        inputCount: c.Inputs?.length || 0,
        outputCount: c.Outputs?.length || 0
      })) || [];
      
      return {
        contents: [
          {
            uri,
            mimeType: "application/json", 
            text: JSON.stringify({
              count: components.length,
              components
            }, null, 2)
          }
        ]
      };
      
    default:
      throw new Error(`Unknown canvas resource: ${uri}`);
  }
}

async function getScriptResource(uri) {
  const scriptPath = uri.replace('gh://scripts/', '');
  const fullPath = path.join('.', 'gh_scripts', scriptPath);
  
  if (!(await fs.fileExists(fullPath))) {
    throw new Error(`Script file not found: ${scriptPath}`);
  }
  
  const content = await fs.readFile(fullPath);
  const mimeType = getScriptMimeType(path.extname(fullPath));
  
  return {
    contents: [
      {
        uri,
        mimeType,
        text: content
      }
    ]
  };
}

async function getLogResource(uri) {
  const store = getStore();
  
  switch (uri) {
    case 'gh://logs/recent':
      const events = store.getEvents(0, null).slice(0, 50);
      return {
        contents: [
          {
            uri,
            mimeType: "application/json",
            text: JSON.stringify({
              timestamp: new Date().toISOString(),
              count: events.length,
              events
            }, null, 2)
          }
        ]
      };
      
    default:
      throw new Error(`Unknown log resource: ${uri}`);
  }
}

async function listScriptResources() {
  const resources = [];
  const scriptsDir = path.join('.', 'gh_scripts');
  
  if (!(await fs.fileExists(scriptsDir))) {
    return resources;
  }
  
  try {
    const allFiles = [
      ...(await fs.listFiles(scriptsDir, '*.py')),
      ...(await fs.listFiles(scriptsDir, '*.cs')),
      ...(await fs.listFiles(scriptsDir, '*.vb'))
    ];
    
    for (const filePath of allFiles) {
      const relativePath = path.relative('.', filePath);
      const scriptPath = relativePath.replace('gh_scripts/', '');
      
      resources.push({
        uri: `gh://scripts/${scriptPath}`,
        name: `Script: ${path.basename(scriptPath)}`,
        description: `Grasshopper script: ${scriptPath}`,
        mimeType: getScriptMimeType(path.extname(scriptPath))
      });
    }
  } catch (error) {
    logger.error('Failed to list script resources', error);
  }
  
  return resources;
}

function getScriptMimeType(ext) {
  switch (ext) {
    case '.py': return 'text/x-python';
    case '.cs': return 'text/x-csharp';  
    case '.vb': return 'text/x-vb';
    default: return 'text/plain';
  }
}