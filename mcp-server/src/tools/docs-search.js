/**
 * RhinoCommon documentation search tool
 * Provides semantic and keyword-based search capabilities for the documentation
 */

import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
import { getLogger } from '../utils/logger.js';
import {
  scoreChunk,
  exactKeywordMatch,
  countKeywordOccurrences,
  extractContext,
  tokenize
} from '../utils/text-similarity.js';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const logger = getLogger();

// Cache for parsed documentation
let documentIndex = null;
let documentLoadTime = null;

/**
 * Parse the RhinoCommon documentation file into searchable chunks
 * @param {string} docsPath - Path to the documentation file
 * @returns {Object[]} Array of document chunks
 */
function parseDocumentation(docsPath) {
  const startTime = Date.now();

  logger.info(`Loading documentation from: ${docsPath}`);

  if (!fs.existsSync(docsPath)) {
    logger.error(`Documentation file not found at: ${docsPath}`);
    return [];
  }

  const content = fs.readFileSync(docsPath, 'utf-8');
  const lines = content.split('\n');

  logger.info(`Read ${lines.length} lines from documentation`);

  const chunks = [];
  let currentNamespace = null;
  let currentChunk = null;
  let chunkLines = [];
  let matchCount = 0;

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];

    // Track namespace for context
    if (line.startsWith('### Namespace')) {
      const match = line.match(/### Namespace `(.+?)`/);
      if (match) {
        currentNamespace = match[1];
      }
    }

    // Start of a new class/interface/enum chunk
    if (line.startsWith('#### ')) {
      // Save previous chunk if exists
      if (currentChunk) {
        currentChunk.text = chunkLines.join('\n');
        currentChunk.lineEnd = i - 1;
        chunks.push(currentChunk);
      }

      // Clean the line (remove any \r from Windows line endings)
      const cleanLine = line.replace(/\r$/, '');

      // Parse the header: "#### class Rhino.Geometry.Collections.BrepCurveList"
      const match = cleanLine.match(/^#### (class|interface|enum|struct)(?:\s+\[.*?\])?\s+(.+)$/);
      if (match) {
        matchCount++;
        if (matchCount <= 3) {
          logger.info(`Matched line ${i}: "${cleanLine}" -> category="${match[1]}", name="${match[2]}"`);
        }
        const [, category, fullName] = match;
        currentChunk = {
          name: fullName.trim(),
          category: category,
          namespace: currentNamespace || 'Unknown',
          lineStart: i + 1,
          lineEnd: null,
          text: ''
        };
        chunkLines = [cleanLine];
      } else {
        if (matchCount < 5) {
          logger.warn(`Failed to match line ${i}: "${cleanLine}" (length: ${cleanLine.length}, charCodes: [${cleanLine.split('').slice(0, 10).map(c => c.charCodeAt(0)).join(',')}])`);
        }
      }
    } else if (currentChunk) {
      // Continue building current chunk
      chunkLines.push(line);
    }
  }

  // Save final chunk
  if (currentChunk) {
    currentChunk.text = chunkLines.join('\n');
    currentChunk.lineEnd = lines.length;
    chunks.push(currentChunk);
  }

  const loadTime = Date.now() - startTime;
  logger.info(`Parsed ${chunks.length} documentation chunks in ${loadTime}ms`);

  return chunks;
}

/**
 * Get or initialize the documentation index
 * @returns {Object[]} Documentation chunks
 */
function getDocumentIndex() {
  if (documentIndex && documentLoadTime) {
    const age = Date.now() - documentLoadTime;
    if (age < 1000 * 60 * 60) { // Cache for 1 hour
      return documentIndex;
    }
  }

  // Find the docs file
  const docsPath = path.resolve(__dirname, '../../../misc/docu-gen/docs/rhinocommon-docs.md');

  if (!fs.existsSync(docsPath)) {
    logger.error(`Documentation file not found: ${docsPath}`);
    throw new Error('Documentation file not found');
  }

  documentIndex = parseDocumentation(docsPath);
  documentLoadTime = Date.now();

  return documentIndex;
}

/**
 * Search documentation using semantic matching
 * @param {string} query - Search query
 * @param {Object} options - Search options
 * @returns {Object[]} Search results
 */
function semanticSearch(query, options = {}) {
  const {
    maxResults = 10,
    contextWindow = 1000,
    minScore = 0.1
  } = options;

  const chunks = getDocumentIndex();
  const startTime = Date.now();

  // Score all chunks
  const scoredChunks = chunks
    .map(chunk => ({
      chunk,
      score: scoreChunk(query, chunk)
    }))
    .filter(item => item.score >= minScore)
    .sort((a, b) => b.score - a.score)
    .slice(0, maxResults);

  // Format results
  const results = scoredChunks.map(({ chunk, score }) => ({
    match: `${chunk.name} (${chunk.category})`,
    score: score,
    context: extractContext(chunk.text, query, contextWindow / 2),
    lineNumber: chunk.lineStart,
    category: chunk.category,
    namespace: chunk.namespace,
    fullName: chunk.name
  }));

  const queryTime = Date.now() - startTime;

  return {
    results,
    totalMatches: scoredChunks.length,
    queryTime: `${queryTime}ms`,
    searchType: 'semantic'
  };
}

/**
 * Search documentation using exact keyword matching
 * @param {string} query - Search query
 * @param {Object} options - Search options
 * @returns {Object[]} Search results
 */
function keywordSearch(query, options = {}) {
  const {
    maxResults = 10,
    contextWindow = 1000
  } = options;

  const chunks = getDocumentIndex();
  const startTime = Date.now();

  // Score chunks by keyword occurrence count
  const scoredChunks = chunks
    .map(chunk => {
      const occurrences = countKeywordOccurrences(query, chunk.text) +
                         countKeywordOccurrences(query, chunk.name) * 5; // Name matches weighted higher
      return {
        chunk,
        score: occurrences
      };
    })
    .filter(item => item.score > 0)
    .sort((a, b) => b.score - a.score)
    .slice(0, maxResults);

  // Format results
  const results = scoredChunks.map(({ chunk, score }) => ({
    match: `${chunk.name} (${chunk.category})`,
    score: score,
    context: extractContext(chunk.text, query, contextWindow / 2),
    lineNumber: chunk.lineStart,
    category: chunk.category,
    namespace: chunk.namespace,
    fullName: chunk.name
  }));

  const queryTime = Date.now() - startTime;

  return {
    results,
    totalMatches: scoredChunks.length,
    queryTime: `${queryTime}ms`,
    searchType: 'keyword'
  };
}

/**
 * Main search function - handles both semantic and keyword search
 * @param {Object} params - Search parameters
 * @returns {Object} Search results
 */
export async function searchRhinoCommonDocs(params) {
  const {
    query,
    semantic = true,
    maxResults = 10,
    contextWindow = 1000
  } = params;

  if (!query || typeof query !== 'string' || query.trim().length === 0) {
    return {
      results: [],
      totalMatches: 0,
      queryTime: '0ms',
      searchType: semantic ? 'semantic' : 'keyword',
      error: 'Query must be a non-empty string'
    };
  }

  try {
    const options = { maxResults, contextWindow };

    if (semantic) {
      return semanticSearch(query, options);
    } else {
      return keywordSearch(query, options);
    }
  } catch (error) {
    logger.error('Search error:', error);
    return {
      results: [],
      totalMatches: 0,
      queryTime: '0ms',
      searchType: semantic ? 'semantic' : 'keyword',
      error: error.message
    };
  }
}

/**
 * Get documentation statistics
 * @returns {Object} Statistics about the documentation
 */
export function getDocumentationStats() {
  try {
    const chunks = getDocumentIndex();

    const stats = {
      totalChunks: chunks.length,
      categories: {},
      namespaces: new Set(),
      cacheAge: documentLoadTime ? `${Math.round((Date.now() - documentLoadTime) / 1000)}s` : 'not loaded'
    };

    for (const chunk of chunks) {
      stats.categories[chunk.category] = (stats.categories[chunk.category] || 0) + 1;
      stats.namespaces.add(chunk.namespace);
    }

    stats.namespaces = Array.from(stats.namespaces);

    return stats;
  } catch (error) {
    logger.error('Error getting documentation stats:', error);
    return {
      error: error.message
    };
  }
}

/**
 * Clear the documentation cache (useful for testing or updates)
 */
export function clearDocumentationCache() {
  documentIndex = null;
  documentLoadTime = null;
  logger.info('Documentation cache cleared');
}
