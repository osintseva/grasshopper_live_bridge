/**
 * Simple JSON querying utility
 * Supports basic JSONPath-like queries
 */

export function queryJson(data, query) {
  if (!query || query === '$') {
    return data;
  }

  // Remove leading $ if present
  const path = query.startsWith('$') ? query.substring(1) : query;
  
  // Split path into segments
  const segments = path.split('.').filter(s => s.length > 0);
  
  let current = data;
  
  for (const segment of segments) {
    // Handle array notation like [0] or [*]
    const arrayMatch = segment.match(/^(.+?)\[(\d+|\*)\]$/);
    
    if (arrayMatch) {
      const key = arrayMatch[1];
      const index = arrayMatch[2];
      
      // Navigate to the array
      if (key) {
        current = current[key];
      }
      
      if (!Array.isArray(current)) {
        return undefined;
      }
      
      // Handle array index or wildcard
      if (index === '*') {
        // Return all array elements
        return current;
      } else {
        current = current[parseInt(index)];
      }
    } else {
      // Handle regular property access
      if (segment === '*') {
        // Return all values of an object
        if (typeof current === 'object' && !Array.isArray(current)) {
          return Object.values(current);
        }
        return current;
      }
      
      current = current?.[segment];
    }
    
    if (current === undefined) {
      return undefined;
    }
  }
  
  return current;
}

/**
 * Filter an array of objects by a condition
 */
export function filterJson(data, key, value) {
  if (!Array.isArray(data)) {
    return [];
  }
  
  return data.filter(item => {
    const itemValue = queryJson(item, key);
    return itemValue === value;
  });
}

/**
 * Extract specific fields from objects
 */
export function extractFields(data, fields) {
  if (!Array.isArray(fields) || fields.length === 0) {
    return data;
  }
  
  const extract = (obj) => {
    const result = {};
    for (const field of fields) {
      const value = queryJson(obj, field);
      if (value !== undefined) {
        result[field] = value;
      }
    }
    return result;
  };
  
  if (Array.isArray(data)) {
    return data.map(extract);
  }
  
  return extract(data);
}

/**
 * Count occurrences of values for a specific field
 */
export function countByField(data, field) {
  if (!Array.isArray(data)) {
    return {};
  }
  
  const counts = {};
  
  for (const item of data) {
    const value = queryJson(item, field);
    if (value !== undefined) {
      const key = String(value);
      counts[key] = (counts[key] || 0) + 1;
    }
  }
  
  return counts;
}

/**
 * Sort array of objects by a field
 */
export function sortByField(data, field, ascending = true) {
  if (!Array.isArray(data)) {
    return data;
  }
  
  return [...data].sort((a, b) => {
    const aValue = queryJson(a, field);
    const bValue = queryJson(b, field);
    
    if (aValue === bValue) return 0;
    if (aValue === undefined) return 1;
    if (bValue === undefined) return -1;
    
    const result = aValue < bValue ? -1 : 1;
    return ascending ? result : -result;
  });
}