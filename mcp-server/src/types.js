// Core types and interfaces for the MCP server

export const EventKind = {
  FS_CHANGE: 'FS_CHANGE',
  APPLIED: 'APPLIED',
  CANVAS_UPDATED: 'CANVAS_UPDATED',
  SELECTION_CHANGED: 'SELECTION_CHANGED',
  DIAGNOSTIC: 'DIAGNOSTIC',
  ERROR: 'ERROR',
  MCP_TOOL_CALL: 'MCP_TOOL_CALL',
  MEDIA_ADDED: 'MEDIA_ADDED'
};

export const ConnectionStatus = {
  DISCONNECTED: 'disconnected',
  CONNECTING: 'connecting',
  CONNECTED: 'connected',
  ERROR: 'error'
};

export class EventLogEntry {
  constructor(kind, data = {}) {
    this.tsClient = Date.now();
    this.tsServer = null;
    this.kind = kind;
    this.correlationId = data.correlationId || null;
    this.componentUuid = data.componentUuid || null;
    this.sha256 = data.sha256 || null;
    this.summary = data.summary || null;
    this.data = data.data || null;
  }
}

export class WebSocketRequest {
  constructor(action, data = {}) {
    this.action = action;
    this.correlationId = `mcp-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
    this.ts = Date.now();
    this.data = data;
  }
}

export class CanvasSnapshot {
  constructor(data) {
    this.timestamp = Date.now();
    this.components = data.Components || [];
    this.connections = data.Connections || [];
    this.selection = data.Selection || [];
    this.metadata = data.Metadata || {};
  }
  
  getComponentCount() {
    return this.components.length;
  }
  
  getSelectedComponents() {
    return this.components.filter(c => this.selection.includes(c.InstanceGuid));
  }
}

