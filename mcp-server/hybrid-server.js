#!/usr/bin/env node

/**
 * Hybrid MCP/HTTP Server for Grasshopper
 * 
 * This server runs both:
 * - MCP server on stdio (for Claude Code)
 * - HTTP server on port 3000 (for VSCode extension and other clients)
 * 
 * Both servers share the same state and WebSocket connection to Grasshopper
 */

import { createMcpServer } from './src/servers/mcp.js';
import { createHttpServer } from './src/servers/http.js';
import { getGrasshopperClient } from './src/websocket-client.js';
import { getStore } from './src/state/store.js';
import { getLogger } from './src/utils/logger.js';

async function main() {
  const store = getStore();
  const logger = getLogger();
  
  // Parse command line arguments
  const args = process.argv.slice(2);
  const httpPort = args.includes('--port') 
    ? parseInt(args[args.indexOf('--port') + 1]) 
    : 3000;
  
  const logLevel = args.includes('--verbose') ? 'verbose' :
                   args.includes('--quiet') ? 'quiet' : 'normal';
  
  // Update settings
  store.updateSettings({
    httpPort,
    logLevel
  });
  
  logger.info('Starting Grasshopper Hybrid MCP/HTTP Server...');
  
  // Initialize Grasshopper WebSocket client
  const ghClient = getGrasshopperClient();
  
  // Set up connection status tracking
  ghClient.on('connected', () => {
    store.setConnectionStatus('connected');
    logger.info('Connected to Grasshopper WebSocket');
  });
  
  ghClient.on('disconnected', () => {
    store.setConnectionStatus('disconnected');
    logger.warn('Disconnected from Grasshopper WebSocket');
  });
  
  ghClient.on('error', (error) => {
    store.setConnectionStatus('error');
    logger.error('Grasshopper WebSocket error', error);
  });
  
  // Subscribe to Grasshopper events
  ghClient.on('event', (event) => {
    logger.verbose(`Grasshopper event: ${event.event}`);
    store.logEvent(`GH_${event.event}`, event.data);
  });
  
  // Try to connect to Grasshopper (non-blocking)
  ghClient.connect().catch(err => {
    logger.warn(`Initial connection to Grasshopper failed: ${err.message}`);
    logger.info('Will retry automatically...');
  });
  
  // Start HTTP server
  const httpServer = createHttpServer({ port: httpPort });
  logger.info(`HTTP API available at http://localhost:${httpPort}`);
  
  // Start MCP server
  const mcpServer = createMcpServer();
  await mcpServer.run();
  
  // Handle shutdown gracefully
  process.on('SIGINT', () => {
    logger.info('Shutting down...');
    ghClient.disconnect();
    httpServer.server.close();
    process.exit(0);
  });
  
  process.on('SIGTERM', () => {
    logger.info('Shutting down...');
    ghClient.disconnect();
    httpServer.server.close();
    process.exit(0);
  });
}

// Run the server
main().catch((error) => {
  console.error('Failed to start hybrid server:', error);
  process.exit(1);
});