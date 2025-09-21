import express from 'express';
import cors from 'cors';
import { getStore } from '../state/store.js';
import { getLogger } from '../utils/logger.js';
import * as canvasTools from '../tools/canvas.js';
import * as scriptTools from '../tools/scripts.js';

const logger = getLogger();

export function createHttpServer(config = {}) {
  const app = express();
  const store = getStore();
  const port = config.port || 3000;
  
  // Middleware
  app.use(cors());
  app.use(express.json());
  
  // Request logging
  app.use((req, res, next) => {
    logger.httpRequest(req.method, req.path);
    next();
  });
  
  // ===== Status & Info Routes =====
  
  app.get('/', (req, res) => {
    res.json({
      name: 'Grasshopper MCP/HTTP Hybrid Server',
      version: '1.0.0',
      endpoints: {
        status: '/api/status',
        canvas: '/api/canvas',
        selection: '/api/selection',
        scripts: '/api/scripts',
        events: '/api/events'
      }
    });
  });
  
  app.get('/api/status', (req, res) => {
    const connectionStatus = store.getConnectionStatus();
    const state = store.getState();
    
    res.json({
      server: 'running',
      grasshopper: connectionStatus,
      snapshots: state.canvasSnapshots.length,
      events: state.eventLog.length,
      scripts: state.scriptMappings.length,
      timestamp: new Date().toISOString()
    });
  });
  
  // ===== Canvas Routes =====
  
  app.get('/api/canvas', async (req, res) => {
    try {
      const includeSelection = req.query.includeSelection === 'true';
      const forceRefresh = req.query.forceRefresh === 'true';
      
      const canvas = await canvasTools.getCanvasState({ 
        includeSelection, 
        forceRefresh 
      });
      
      res.json({
        status: 'success',
        data: canvas
      });
    } catch (error) {
      logger.error('HTTP: Failed to get canvas', error);
      res.status(500).json({
        status: 'error',
        message: error.message
      });
    }
  });
  
  app.get('/api/selection', async (req, res) => {
    try {
      const forceRefresh = req.query.forceRefresh === 'true';
      const selection = await canvasTools.getSelection({ forceRefresh });
      
      res.json({
        status: 'success',
        data: selection
      });
    } catch (error) {
      logger.error('HTTP: Failed to get selection', error);
      res.status(500).json({
        status: 'error',
        message: error.message
      });
    }
  });
  
  app.post('/api/canvas/query', async (req, res) => {
    try {
      const { query, source, filePath } = req.body;
      const result = await canvasTools.queryCanvasPseudocode({
        query,
        source,
        filePath
      });
      
      res.json({
        status: 'success',
        data: result
      });
    } catch (error) {
      logger.error('HTTP: Failed to query canvas', error);
      res.status(500).json({
        status: 'error',
        message: error.message
      });
    }
  });
  
  app.get('/api/canvas/statistics', async (req, res) => {
    try {
      const stats = await canvasTools.getCanvasStatistics();
      
      res.json({
        status: 'success',
        data: stats
      });
    } catch (error) {
      logger.error('HTTP: Failed to get canvas statistics', error);
      res.status(500).json({
        status: 'error',
        message: error.message
      });
    }
  });
  
  app.get('/api/components/:uuid', async (req, res) => {
    try {
      const componentUuid = req.params.uuid;
      const info = await canvasTools.getComponentInfo({ componentUuid });
      
      if (!info.found) {
        res.status(404).json({
          status: 'error',
          message: 'Component not found'
        });
        return;
      }
      
      res.json({
        status: 'success',
        data: info
      });
    } catch (error) {
      logger.error('HTTP: Failed to get component info', error);
      res.status(500).json({
        status: 'error',
        message: error.message
      });
    }
  });
  
  // ===== Script Routes =====
  
  app.get('/api/scripts', async (req, res) => {
    try {
      const includeContent = req.query.includeContent === 'true';
      const scripts = await scriptTools.listScripts({ includeContent });
      
      res.json({
        status: 'success',
        data: scripts
      });
    } catch (error) {
      logger.error('HTTP: Failed to list scripts', error);
      res.status(500).json({
        status: 'error',
        message: error.message
      });
    }
  });
  
  app.post('/api/scripts/create', async (req, res) => {
    try {
      const { componentUuid, language, nameHint } = req.body;
      
      if (!componentUuid) {
        res.status(400).json({
          status: 'error',
          message: 'componentUuid is required'
        });
        return;
      }
      
      const result = await scriptTools.createScriptFile({
        componentUuid,
        language,
        nameHint
      });
      
      res.json({
        status: 'success',
        data: result
      });
    } catch (error) {
      logger.error('HTTP: Failed to create script', error);
      res.status(500).json({
        status: 'error',
        message: error.message
      });
    }
  });
  
  app.post('/api/scripts/push', async (req, res) => {
    try {
      const { componentUuid, filePath, language } = req.body;
      
      if (!componentUuid) {
        res.status(400).json({
          status: 'error',
          message: 'componentUuid is required'
        });
        return;
      }
      
      const result = await scriptTools.pushScriptUpdate({
        componentUuid,
        filePath,
        language
      });
      
      res.json({
        status: 'success',
        data: result
      });
    } catch (error) {
      logger.error('HTTP: Failed to push script', error);
      res.status(500).json({
        status: 'error',
        message: error.message
      });
    }
  });
  
  app.delete('/api/scripts/:uuid', async (req, res) => {
    try {
      const componentUuid = req.params.uuid;
      const result = await scriptTools.deleteScriptFile({ componentUuid });
      
      res.json({
        status: 'success',
        data: result
      });
    } catch (error) {
      logger.error('HTTP: Failed to delete script', error);
      res.status(500).json({
        status: 'error',
        message: error.message
      });
    }
  });
  
  // ===== Event Routes =====
  
  app.get('/api/events', (req, res) => {
    try {
      const since = parseInt(req.query.since) || 0;
      const kind = req.query.kind || null;
      const events = store.getEvents(since, kind);
      
      res.json({
        status: 'success',
        data: events,
        count: events.length
      });
    } catch (error) {
      logger.error('HTTP: Failed to get events', error);
      res.status(500).json({
        status: 'error',
        message: error.message
      });
    }
  });
  
  // ===== Snapshot Routes =====
  
  app.get('/api/snapshots', (req, res) => {
    try {
      const state = store.getState();
      const snapshots = state.canvasSnapshots.map(s => ({
        id: s.id,
        timestamp: s.timestamp,
        componentCount: s.Components?.length || 0
      }));
      
      res.json({
        status: 'success',
        data: snapshots
      });
    } catch (error) {
      logger.error('HTTP: Failed to get snapshots', error);
      res.status(500).json({
        status: 'error',
        message: error.message
      });
    }
  });
  
  app.get('/api/snapshots/:id', (req, res) => {
    try {
      const snapshot = store.getSnapshot(req.params.id);
      
      if (!snapshot) {
        res.status(404).json({
          status: 'error',
          message: 'Snapshot not found'
        });
        return;
      }
      
      res.json({
        status: 'success',
        data: snapshot
      });
    } catch (error) {
      logger.error('HTTP: Failed to get snapshot', error);
      res.status(500).json({
        status: 'error',
        message: error.message
      });
    }
  });
  
  // Start server
  const server = app.listen(port, () => {
    logger.info(`HTTP server listening on http://localhost:${port}`);
  });
  
  return {
    app,
    server,
    port
  };
}

export default createHttpServer;