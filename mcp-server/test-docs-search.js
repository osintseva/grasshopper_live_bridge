/**
 * Test script for RhinoCommon documentation search
 * Run with: node test-docs-search.js
 */

import { searchRhinoCommonDocs, getDocumentationStats } from './src/tools/docs-search.js';

console.log('='.repeat(80));
console.log('RhinoCommon Documentation Search Test');
console.log('='.repeat(80));
console.log();

// Test 1: Get documentation statistics
console.log('Test 1: Documentation Statistics');
console.log('-'.repeat(80));
const stats = getDocumentationStats();
console.log(JSON.stringify(stats, null, 2));
console.log();

// Test 2: Semantic search for "Brep Line Intersection"
console.log('Test 2: Semantic Search - "Brep Line Intersection"');
console.log('-'.repeat(80));
const result1 = await searchRhinoCommonDocs({
  query: 'Brep Line Intersection',
  semantic: true,
  maxResults: 5,
  contextWindow: 500
});

console.log(`Found ${result1.totalMatches} matches in ${result1.queryTime}`);
console.log(`Search type: ${result1.searchType}`);
console.log();

result1.results.forEach((r, i) => {
  console.log(`${i + 1}. ${r.match}`);
  console.log(`   Score: ${r.score.toFixed(2)} | Namespace: ${r.namespace}`);
  console.log(`   Line: ${r.lineNumber}`);
  console.log(`   Context: ${r.context.substring(0, 150)}...`);
  console.log();
});

// Test 3: Semantic search for "surface intersect"
console.log('Test 3: Semantic Search - "surface intersect"');
console.log('-'.repeat(80));
const result2 = await searchRhinoCommonDocs({
  query: 'surface intersect',
  semantic: true,
  maxResults: 5,
  contextWindow: 500
});

console.log(`Found ${result2.totalMatches} matches in ${result2.queryTime}`);
console.log();

result2.results.slice(0, 3).forEach((r, i) => {
  console.log(`${i + 1}. ${r.match} (Score: ${r.score.toFixed(2)})`);
});
console.log();

// Test 4: Keyword search for "GetBoundingBox"
console.log('Test 4: Keyword Search - "GetBoundingBox"');
console.log('-'.repeat(80));
const result3 = await searchRhinoCommonDocs({
  query: 'GetBoundingBox',
  semantic: false,
  maxResults: 5,
  contextWindow: 300
});

console.log(`Found ${result3.totalMatches} matches in ${result3.queryTime}`);
console.log();

result3.results.slice(0, 3).forEach((r, i) => {
  console.log(`${i + 1}. ${r.match} (Score: ${r.score})`);
});
console.log();

// Test 5: Semantic search for "point on curve"
console.log('Test 5: Semantic Search - "point on curve"');
console.log('-'.repeat(80));
const result4 = await searchRhinoCommonDocs({
  query: 'point on curve',
  semantic: true,
  maxResults: 5,
  contextWindow: 500
});

console.log(`Found ${result4.totalMatches} matches in ${result4.queryTime}`);
console.log();

result4.results.slice(0, 3).forEach((r, i) => {
  console.log(`${i + 1}. ${r.match} (Score: ${r.score.toFixed(2)})`);
});
console.log();

console.log('='.repeat(80));
console.log('All tests completed!');
console.log('='.repeat(80));
