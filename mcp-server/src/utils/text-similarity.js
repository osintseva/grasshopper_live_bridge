/**
 * Text similarity and tokenization utilities for semantic search
 * No external dependencies - pure JavaScript implementation
 */

/**
 * Tokenize text into lowercase words, removing punctuation
 * @param {string} text - Text to tokenize
 * @returns {string[]} Array of tokens
 */
export function tokenize(text) {
  if (!text || typeof text !== 'string') return [];

  return text
    .toLowerCase()
    .replace(/[^a-z0-9\s]/gi, ' ') // Replace punctuation with spaces
    .split(/\s+/)
    .filter(token => token.length > 0);
}

/**
 * Split CamelCase or PascalCase strings into separate words
 * Examples:
 *   BrepCurveIntersection -> ['Brep', 'Curve', 'Intersection']
 *   getPointAt -> ['get', 'Point', 'At']
 *   XMLHttpRequest -> ['XML', 'Http', 'Request']
 *
 * @param {string} text - CamelCase/PascalCase text
 * @returns {string[]} Array of split words
 */
export function splitCamelCase(text) {
  if (!text || typeof text !== 'string') return [];

  // Handle consecutive uppercase letters (like "XMLHttp" -> "XML Http")
  // Then split on uppercase letters preceded by lowercase
  return text
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')  // XMLHttp -> XML Http
    .replace(/([a-z\d])([A-Z])/g, '$1 $2')       // camelCase -> camel Case
    .replace(/([a-z])(\d)/gi, '$1 $2')           // test123 -> test 123
    .split(/\s+/)
    .filter(word => word.length > 0);
}

/**
 * Extract all identifiers (class names, method names, variables) from text
 * and split them using camelCase splitting
 *
 * @param {string} text - Text containing identifiers
 * @returns {string[]} Array of split identifier tokens
 */
export function extractAndSplitIdentifiers(text) {
  if (!text || typeof text !== 'string') return [];

  // Match word sequences that look like identifiers (alphanumeric, starting with letter)
  const identifierPattern = /\b[A-Za-z][A-Za-z0-9_]*\b/g;
  const identifiers = text.match(identifierPattern) || [];

  const tokens = [];
  for (const identifier of identifiers) {
    const split = splitCamelCase(identifier);
    tokens.push(...split.map(t => t.toLowerCase()));
  }

  return [...new Set(tokens)]; // Remove duplicates
}

/**
 * Calculate a simple similarity score between two token sets
 * Uses weighted scoring for different match types
 *
 * @param {string[]} queryTokens - Tokens from search query
 * @param {string[]} targetTokens - Tokens from target text
 * @param {Object} weights - Weight configuration
 * @returns {number} Similarity score
 */
export function calculateTokenSimilarity(queryTokens, targetTokens, weights = {}) {
  const defaultWeights = {
    exactMatch: 5,
    partialMatch: 2,
    substringMatch: 1
  };

  const w = { ...defaultWeights, ...weights };
  let score = 0;

  const targetSet = new Set(targetTokens.map(t => t.toLowerCase()));
  const targetLower = targetTokens.map(t => t.toLowerCase());

  for (const queryToken of queryTokens) {
    const queryLower = queryToken.toLowerCase();

    // Exact match
    if (targetSet.has(queryLower)) {
      score += w.exactMatch;
      continue;
    }

    // Partial match (query token is part of target token)
    let foundPartial = false;
    for (const targetToken of targetLower) {
      if (targetToken.includes(queryLower) || queryLower.includes(targetToken)) {
        score += w.partialMatch;
        foundPartial = true;
        break;
      }
    }

    if (foundPartial) continue;

    // Substring match (query token shares significant substring with target)
    for (const targetToken of targetLower) {
      const longestCommon = longestCommonSubstring(queryLower, targetToken);
      if (longestCommon.length >= Math.min(queryLower.length, targetToken.length) * 0.6) {
        score += w.substringMatch;
        break;
      }
    }
  }

  // Normalize by query length to favor matches with more query tokens
  return score / Math.max(queryTokens.length, 1);
}

/**
 * Find longest common substring between two strings
 * @param {string} str1 - First string
 * @param {string} str2 - Second string
 * @returns {string} Longest common substring
 */
function longestCommonSubstring(str1, str2) {
  if (!str1 || !str2) return '';

  let longest = '';
  const matrix = Array(str1.length + 1).fill(null)
    .map(() => Array(str2.length + 1).fill(0));

  for (let i = 1; i <= str1.length; i++) {
    for (let j = 1; j <= str2.length; j++) {
      if (str1[i - 1] === str2[j - 1]) {
        matrix[i][j] = matrix[i - 1][j - 1] + 1;
        if (matrix[i][j] > longest.length) {
          longest = str1.substring(i - matrix[i][j], i);
        }
      }
    }
  }

  return longest;
}

/**
 * Score a document chunk against a query
 * Considers multiple factors:
 * - Token matches in identifiers (with camelCase splitting)
 * - Token matches in regular text
 * - Position of matches (title/name vs description)
 *
 * @param {string} query - Search query
 * @param {Object} chunk - Document chunk with text and metadata
 * @param {Object} options - Scoring options
 * @returns {number} Relevance score
 */
export function scoreChunk(query, chunk, options = {}) {
  const {
    nameWeight = 3,      // Weight for matches in names/titles
    textWeight = 1,      // Weight for matches in body text
    identifierBoost = 2  // Multiplier for identifier matches
  } = options;

  const queryTokens = tokenize(query);
  const queryIdentifierTokens = extractAndSplitIdentifiers(query);

  let score = 0;

  // Score matches in chunk name/title (if present)
  if (chunk.name) {
    const nameTokens = tokenize(chunk.name);
    const nameIdentifierTokens = extractAndSplitIdentifiers(chunk.name);

    score += calculateTokenSimilarity(queryTokens, nameTokens) * nameWeight;
    score += calculateTokenSimilarity(queryIdentifierTokens, nameIdentifierTokens)
             * nameWeight * identifierBoost;
  }

  // Score matches in chunk text/content
  if (chunk.text) {
    const textTokens = tokenize(chunk.text);
    const textIdentifierTokens = extractAndSplitIdentifiers(chunk.text);

    score += calculateTokenSimilarity(queryTokens, textTokens) * textWeight;
    score += calculateTokenSimilarity(queryIdentifierTokens, textIdentifierTokens)
             * textWeight * identifierBoost;
  }

  return score;
}

/**
 * Simple exact keyword search (for non-semantic mode)
 * @param {string} query - Search query
 * @param {string} text - Text to search in
 * @returns {boolean} True if all query words are found in text
 */
export function exactKeywordMatch(query, text) {
  if (!query || !text) return false;

  const queryTokens = tokenize(query);
  const textLower = text.toLowerCase();

  return queryTokens.every(token => textLower.includes(token));
}

/**
 * Count keyword occurrences in text
 * @param {string} query - Search query
 * @param {string} text - Text to search in
 * @returns {number} Number of query token occurrences
 */
export function countKeywordOccurrences(query, text) {
  if (!query || !text) return 0;

  const queryTokens = tokenize(query);
  const textLower = text.toLowerCase();

  let count = 0;
  for (const token of queryTokens) {
    const matches = textLower.match(new RegExp(token, 'g'));
    if (matches) {
      count += matches.length;
    }
  }

  return count;
}

/**
 * Extract context window around matches
 * @param {string} text - Full text
 * @param {string} query - Query to find
 * @param {number} windowSize - Characters to include before and after
 * @returns {string} Context string with "..." indicators
 */
export function extractContext(text, query, windowSize = 500) {
  if (!text || !query) return text || '';

  const queryTokens = tokenize(query);
  if (queryTokens.length === 0) return text.substring(0, windowSize * 2);

  // Find first occurrence of any query token
  const textLower = text.toLowerCase();
  let firstMatchIndex = -1;

  for (const token of queryTokens) {
    const index = textLower.indexOf(token);
    if (index !== -1 && (firstMatchIndex === -1 || index < firstMatchIndex)) {
      firstMatchIndex = index;
    }
  }

  if (firstMatchIndex === -1) {
    // No matches found, return start of text
    return text.substring(0, windowSize * 2) + (text.length > windowSize * 2 ? '...' : '');
  }

  // Extract window around first match
  const start = Math.max(0, firstMatchIndex - windowSize);
  const end = Math.min(text.length, firstMatchIndex + windowSize);

  let context = text.substring(start, end);

  if (start > 0) context = '...' + context;
  if (end < text.length) context = context + '...';

  return context;
}
