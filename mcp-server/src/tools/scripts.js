import { getStore } from '../state/store.js';
import { getLogger } from '../utils/logger.js';
import { getGrasshopperClient } from '../websocket-client.js';
import * as fs from '../utils/file-system.js';
import path from 'path';

const logger = getLogger();
const store = getStore();

/**
 * Script management tools for MCP and HTTP
 */

export async function createScriptFile(args = {}) {
  const { componentUuid, language = 'python', nameHint = null, projectDir = '.' } = args;
  
  if (!componentUuid) {
    throw new Error('componentUuid is required');
  }
  
  logger.toolCall('createScriptFile', args);
  
  try {
    // Check if script already exists for this component
    const existingMapping = store.getScriptMapping(componentUuid);
    if (existingMapping) {
      return {
        created: false,
        exists: true,
        filePath: existingMapping.filePath,
        message: 'Script file already exists for this component'
      };
    }
    
    // Create the script file
    const result = await fs.createScriptFile(componentUuid, language, nameHint, projectDir);
    
    // Store the mapping
    store.mapScript(componentUuid, result.filePath, language);
    
    // Log the event
    store.logEvent('SCRIPT_CREATED', {
      componentUuid,
      filePath: result.filePath,
      language
    });
    
    return {
      created: true,
      ...result
    };
  } catch (error) {
    logger.error('Failed to create script file', error);
    throw error;
  }
}

export async function pushScriptUpdate(args = {}) {
  const { componentUuid, filePath = null, language = null } = args;
  
  if (!componentUuid) {
    throw new Error('componentUuid is required');
  }
  
  logger.toolCall('pushScriptUpdate', args);
  
  try {
    // Get file path from mapping if not provided
    let scriptPath = filePath;
    let scriptLanguage = language;
    
    if (!scriptPath) {
      const mapping = store.getScriptMapping(componentUuid);
      if (!mapping) {
        throw new Error(`No script mapping found for component ${componentUuid}`);
      }
      scriptPath = mapping.filePath;
      scriptLanguage = scriptLanguage || mapping.language;
    }
    
    // Read the script content
    const content = await fs.readFile(scriptPath);
    if (!content) {
      throw new Error(`Failed to read script file: ${scriptPath}`);
    }
    
    // Calculate SHA256
    const sha256 = await fs.calculateSHA256(scriptPath);
    
    // Get file stats
    const stats = await fs.getFileStats(scriptPath);
    
    // Push to Grasshopper
    const client = getGrasshopperClient();
    const response = await client.pushScriptUpdate(
      componentUuid,
      scriptPath,
      scriptLanguage || 'python',
      sha256
    );
    
    // Log the event
    store.logEvent('SCRIPT_PUSHED', {
      componentUuid,
      filePath: scriptPath,
      sha256,
      fileSize: stats?.size || 0
    });
    
    return {
      pushed: true,
      componentUuid,
      filePath: scriptPath,
      sha256,
      response
    };
  } catch (error) {
    logger.error('Failed to push script update', error);
    throw error;
  }
}

export async function listScripts(args = {}) {
  const { projectDir = '.', includeContent = false } = args;
  
  logger.toolCall('listScripts', args);
  
  try {
    const scriptsDir = path.join(projectDir, 'gh_scripts');
    
    // Check if scripts directory exists
    if (!(await fs.fileExists(scriptsDir))) {
      return {
        scripts: [],
        directory: scriptsDir,
        exists: false
      };
    }
    
    // List all script files
    const pythonFiles = await fs.listFiles(scriptsDir, '*.py');
    const csFiles = await fs.listFiles(scriptsDir, '*.cs');
    const vbFiles = await fs.listFiles(scriptsDir, '*.vb');
    
    const allFiles = [...pythonFiles, ...csFiles, ...vbFiles];
    
    // Build script info
    const scripts = [];
    
    for (const filePath of allFiles) {
      const stats = await fs.getFileStats(filePath);
      const content = includeContent ? await fs.readFile(filePath) : null;
      const uuid = content ? fs.extractUuidFromContent(content) : null;
      
      // Get mapping info
      const mapping = uuid ? store.getScriptMapping(uuid) : null;
      
      scripts.push({
        filePath,
        fileName: path.basename(filePath),
        language: path.extname(filePath).substring(1),
        size: stats?.size || 0,
        modified: stats?.modified || null,
        componentUuid: uuid,
        isMapped: !!mapping,
        content: includeContent ? content : undefined
      });
    }
    
    // Sort by modified date
    scripts.sort((a, b) => {
      if (!a.modified || !b.modified) return 0;
      return new Date(b.modified) - new Date(a.modified);
    });
    
    return {
      scripts,
      directory: scriptsDir,
      exists: true,
      count: scripts.length
    };
  } catch (error) {
    logger.error('Failed to list scripts', error);
    throw error;
  }
}

export async function confirmLastUpdate(args = {}) {
  const { componentUuid, sha256 = null } = args;
  
  if (!componentUuid) {
    throw new Error('componentUuid is required');
  }
  
  logger.toolCall('confirmLastUpdate', args);
  
  try {
    // Get the last push event for this component
    const events = store.getEvents(0, 'SCRIPT_PUSHED');
    const lastPush = events.find(e => e.componentUuid === componentUuid);
    
    if (!lastPush) {
      return {
        confirmed: false,
        message: 'No script push found for this component'
      };
    }
    
    // Check SHA256 if provided
    if (sha256 && lastPush.sha256 !== sha256) {
      return {
        confirmed: false,
        message: 'SHA256 mismatch',
        expected: sha256,
        actual: lastPush.sha256
      };
    }
    
    // Check for applied event
    const appliedEvents = store.getEvents(lastPush.timestamp, 'APPLIED');
    const wasApplied = appliedEvents.some(e => e.componentUuid === componentUuid);
    
    return {
      confirmed: true,
      lastPush: {
        timestamp: lastPush.timestamp,
        sha256: lastPush.sha256,
        filePath: lastPush.filePath
      },
      applied: wasApplied
    };
  } catch (error) {
    logger.error('Failed to confirm last update', error);
    throw error;
  }
}

export async function deleteScriptFile(args = {}) {
  const { componentUuid, filePath = null } = args;
  
  if (!componentUuid && !filePath) {
    throw new Error('Either componentUuid or filePath is required');
  }
  
  logger.toolCall('deleteScriptFile', args);
  
  try {
    let scriptPath = filePath;
    
    // Get file path from mapping if using componentUuid
    if (componentUuid && !scriptPath) {
      const mapping = store.getScriptMapping(componentUuid);
      if (!mapping) {
        return {
          deleted: false,
          message: 'No script mapping found for this component'
        };
      }
      scriptPath = mapping.filePath;
    }
    
    // Check if file exists
    if (!(await fs.fileExists(scriptPath))) {
      return {
        deleted: false,
        message: 'Script file does not exist'
      };
    }
    
    // Delete the file
    const success = await fs.deleteFile(scriptPath);
    
    if (success) {
      // Remove mapping if using componentUuid
      if (componentUuid) {
        store.state.scriptMappings.delete(componentUuid);
      }
      
      // Log the event
      store.logEvent('SCRIPT_DELETED', {
        componentUuid,
        filePath: scriptPath
      });
    }
    
    return {
      deleted: success,
      filePath: scriptPath
    };
  } catch (error) {
    logger.error('Failed to delete script file', error);
    throw error;
  }
}