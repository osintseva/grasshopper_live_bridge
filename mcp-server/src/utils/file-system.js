import fs from 'fs/promises';
import path from 'path';
import crypto from 'crypto';

/**
 * File system utilities
 */

export async function ensureDirectory(dirPath) {
  try {
    await fs.mkdir(dirPath, { recursive: true });
    return true;
  } catch (error) {
    console.error(`Failed to create directory ${dirPath}:`, error);
    return false;
  }
}

export async function fileExists(filePath) {
  try {
    await fs.access(filePath);
    return true;
  } catch {
    return false;
  }
}

export async function readFile(filePath) {
  try {
    const content = await fs.readFile(filePath, 'utf-8');
    return content;
  } catch (error) {
    console.error(`Failed to read file ${filePath}:`, error);
    return null;
  }
}

export async function writeFile(filePath, content) {
  try {
    await ensureDirectory(path.dirname(filePath));
    await fs.writeFile(filePath, content, 'utf-8');
    return true;
  } catch (error) {
    console.error(`Failed to write file ${filePath}:`, error);
    return false;
  }
}

export async function deleteFile(filePath) {
  try {
    await fs.unlink(filePath);
    return true;
  } catch (error) {
    console.error(`Failed to delete file ${filePath}:`, error);
    return false;
  }
}

export async function listFiles(dirPath, pattern = '*') {
  try {
    const files = await fs.readdir(dirPath, { withFileTypes: true });
    
    // Simple pattern matching (supports *.ext)
    const regex = pattern === '*' 
      ? /.*/ 
      : new RegExp(pattern.replace('*', '.*').replace('.', '\\.'));
    
    return files
      .filter(dirent => dirent.isFile() && regex.test(dirent.name))
      .map(dirent => path.join(dirPath, dirent.name));
  } catch (error) {
    console.error(`Failed to list files in ${dirPath}:`, error);
    return [];
  }
}

export async function getFileStats(filePath) {
  try {
    const stats = await fs.stat(filePath);
    return {
      size: stats.size,
      created: stats.birthtime,
      modified: stats.mtime,
      isDirectory: stats.isDirectory(),
      isFile: stats.isFile()
    };
  } catch (error) {
    console.error(`Failed to get stats for ${filePath}:`, error);
    return null;
  }
}

export async function calculateSHA256(filePath) {
  try {
    const content = await fs.readFile(filePath);
    const hash = crypto.createHash('sha256');
    hash.update(content);
    return hash.digest('hex');
  } catch (error) {
    console.error(`Failed to calculate SHA256 for ${filePath}:`, error);
    return null;
  }
}

export function extractUuidFromContent(content) {
  // Look for UUID in various comment formats
  const patterns = [
    /# Component UUID: ([a-f0-9-]{36})/i,
    /# UUID: ([a-f0-9-]{36})/i,
    /\/\/ Component UUID: ([a-f0-9-]{36})/i,
    /\/\/ UUID: ([a-f0-9-]{36})/i,
    /' Component UUID: ([a-f0-9-]{36})/i
  ];
  
  for (const pattern of patterns) {
    const match = content.match(pattern);
    if (match) {
      return match[1];
    }
  }
  
  return null;
}


export async function watchDirectory(dirPath, callback, options = {}) {
  const { recursive = true, persistent = true } = options;
  
  try {
    const watcher = fs.watch(dirPath, { recursive, persistent });
    
    for await (const event of watcher) {
      callback(event);
    }
  } catch (error) {
    console.error(`Failed to watch directory ${dirPath}:`, error);
  }
}