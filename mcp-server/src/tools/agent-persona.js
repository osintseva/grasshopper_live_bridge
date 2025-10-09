/**
 * Agent Persona System
 *
 * Provides dynamic agent persona construction through three types of content:
 * - Roles: Agent identity and expertise definitions
 * - Recipes: Step-by-step workflow checklists
 * - Docs: Reference documentation and specifications
 *
 * All content uses pseudo-XML formatting for structured sections.
 */

import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
import { getLogger } from '../utils/logger.js';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const logger = getLogger();

// Cache for loaded content (1-hour TTL)
let roleCache = new Map();
let recipeCache = new Map();
let docCache = new Map();
let loadTime = null;

// Base path to agent-persona content
const PERSONA_BASE_PATH = path.resolve(__dirname, '../../../misc/agent-persona');

/**
 * Check if cache is still valid (1-hour TTL)
 */
function isCacheValid() {
  if (!loadTime) return false;
  const age = Date.now() - loadTime;
  return age < 1000 * 60 * 60; // 1 hour
}

/**
 * Load content from filesystem
 * @param {string} type - Content type (roles/recipes/docs)
 * @param {string} key - Content key/filename
 * @returns {string} Markdown content
 */
function loadContent(type, key) {
  const filePath = path.join(PERSONA_BASE_PATH, type, `${key}.md`);

  if (!fs.existsSync(filePath)) {
    const available = getAvailableKeys(type);
    throw new Error(
      `${capitalizeType(type)} not found: "${key}"\n` +
      `Available ${type}: ${available.join(', ')}`
    );
  }

  try {
    const content = fs.readFileSync(filePath, 'utf-8');
    logger.verbose(`Loaded ${type}/${key} (${content.length} chars)`);
    return content;
  } catch (error) {
    logger.error(`Failed to load ${type}/${key}:`, error);
    throw new Error(`Failed to load ${type}/${key}: ${error.message}`);
  }
}

/**
 * Get list of available keys for a content type
 * @param {string} type - Content type (roles/recipes/docs)
 * @returns {string[]} Array of available keys
 */
function getAvailableKeys(type) {
  const dirPath = path.join(PERSONA_BASE_PATH, type);

  if (!fs.existsSync(dirPath)) {
    return [];
  }

  try {
    const files = fs.readdirSync(dirPath);
    return files
      .filter(f => f.endsWith('.md'))
      .map(f => f.replace('.md', ''))
      .sort();
  } catch (error) {
    logger.error(`Failed to list ${type}:`, error);
    return [];
  }
}

/**
 * Capitalize content type for error messages
 */
function capitalizeType(type) {
  const typeMap = {
    'roles': 'Role',
    'recipes': 'Recipe',
    'docs': 'Doc'
  };
  return typeMap[type] || type;
}

/**
 * Update load time for cache TTL tracking
 */
function updateLoadTime() {
  loadTime = Date.now();
}

/**
 * Get an agent role/persona
 * @param {Object} params - Parameters
 * @param {string} params.key - Role key
 * @returns {string} Role markdown content
 */
export async function getRole(params) {
  const { key } = params;

  if (!key || typeof key !== 'string') {
    throw new Error('Role key is required and must be a string');
  }

  logger.toolCall('getRole', params);

  // Check cache
  if (isCacheValid() && roleCache.has(key)) {
    logger.verbose(`Role cache hit: ${key}`);
    return roleCache.get(key);
  }

  // Load from filesystem
  const content = loadContent('roles', key);
  roleCache.set(key, content);
  updateLoadTime();

  return content;
}

/**
 * Get a workflow recipe
 * @param {Object} params - Parameters
 * @param {string} params.key - Recipe key
 * @returns {string} Recipe markdown content
 */
export async function getRecipe(params) {
  const { key } = params;

  if (!key || typeof key !== 'string') {
    throw new Error('Recipe key is required and must be a string');
  }

  logger.toolCall('getRecipe', params);

  // Check cache
  if (isCacheValid() && recipeCache.has(key)) {
    logger.verbose(`Recipe cache hit: ${key}`);
    return recipeCache.get(key);
  }

  // Load from filesystem
  const content = loadContent('recipes', key);
  recipeCache.set(key, content);
  updateLoadTime();

  return content;
}

/**
 * Get reference documentation
 * @param {Object} params - Parameters
 * @param {string} params.key - Doc key
 * @returns {string} Doc markdown content
 */
export async function getDoc(params) {
  const { key } = params;

  if (!key || typeof key !== 'string') {
    throw new Error('Doc key is required and must be a string');
  }

  logger.toolCall('getDoc', params);

  // Check cache
  if (isCacheValid() && docCache.has(key)) {
    logger.verbose(`Doc cache hit: ${key}`);
    return docCache.get(key);
  }

  // Load from filesystem
  const content = loadContent('docs', key);
  docCache.set(key, content);
  updateLoadTime();

  return content;
}

/**
 * Clear all caches (useful for testing or content updates)
 */
export function clearCache() {
  roleCache.clear();
  recipeCache.clear();
  docCache.clear();
  loadTime = null;
  logger.info('Agent persona cache cleared');
}

/**
 * Get statistics about the persona system
 * @returns {Object} Statistics
 */
export function getStats() {
  return {
    roles: {
      cached: roleCache.size,
      available: getAvailableKeys('roles')
    },
    recipes: {
      cached: recipeCache.size,
      available: getAvailableKeys('recipes')
    },
    docs: {
      cached: docCache.size,
      available: getAvailableKeys('docs')
    },
    cacheAge: loadTime ? `${Math.round((Date.now() - loadTime) / 1000)}s` : 'not loaded',
    cacheValid: isCacheValid()
  };
}
