import { EventEmitter } from 'events';

class StateStore extends EventEmitter {
  constructor() {
    super();
    this.state = {
      // Canvas snapshots with timestamps
      canvasSnapshots: [],
      maxSnapshots: 10,
      
      // Event log
      eventLog: [],
      maxEvents: 1000,
      
      
      // Connection status
      connectionStatus: 'disconnected',
      lastConnectionTime: null,
      
      // Temporary data (with TTL)
      cache: new Map(),
      
      // Settings
      settings: {
        grasshopperHost: '127.0.0.1',
        grasshopperPort: 8181,
        httpPort: 3000,
        logLevel: 'normal'
      }
    };
  }

  // Canvas snapshots
  addCanvasSnapshot(snapshot) {
    const timestampedSnapshot = {
      ...snapshot,
      timestamp: Date.now(),
      id: `snapshot-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`
    };
    
    this.state.canvasSnapshots.unshift(timestampedSnapshot);
    
    // Keep only the last N snapshots
    if (this.state.canvasSnapshots.length > this.state.maxSnapshots) {
      this.state.canvasSnapshots = this.state.canvasSnapshots.slice(0, this.state.maxSnapshots);
    }
    
    this.emit('snapshotAdded', timestampedSnapshot);
    return timestampedSnapshot;
  }

  getLatestSnapshot() {
    return this.state.canvasSnapshots[0] || null;
  }

  getSnapshot(id) {
    return this.state.canvasSnapshots.find(s => s.id === id);
  }

  clearSnapshots() {
    this.state.canvasSnapshots = [];
    this.emit('snapshotsCleared');
  }

  // Event logging
  logEvent(kind, data = {}) {
    const event = {
      id: `event-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`,
      timestamp: Date.now(),
      kind,
      ...data
    };
    
    this.state.eventLog.unshift(event);
    
    // Keep only the last N events
    if (this.state.eventLog.length > this.state.maxEvents) {
      this.state.eventLog = this.state.eventLog.slice(0, this.state.maxEvents);
    }
    
    this.emit('eventLogged', event);
    return event;
  }

  getEvents(since = 0, kind = null) {
    let events = this.state.eventLog;
    
    if (since > 0) {
      events = events.filter(e => e.timestamp > since);
    }
    
    if (kind) {
      events = events.filter(e => e.kind === kind);
    }
    
    return events;
  }


  // Connection status
  setConnectionStatus(status) {
    const oldStatus = this.state.connectionStatus;
    this.state.connectionStatus = status;
    
    if (status === 'connected') {
      this.state.lastConnectionTime = Date.now();
    }
    
    if (oldStatus !== status) {
      this.emit('connectionStatusChanged', { oldStatus, newStatus: status });
      this.logEvent('CONNECTION_STATUS', { status });
    }
  }

  getConnectionStatus() {
    return {
      status: this.state.connectionStatus,
      lastConnectionTime: this.state.lastConnectionTime
    };
  }

  // Cache with TTL
  setCache(key, value, ttlMs = 60000) {
    const entry = {
      value,
      expiresAt: Date.now() + ttlMs
    };
    
    this.state.cache.set(key, entry);
    
    // Auto-cleanup
    setTimeout(() => {
      const cached = this.state.cache.get(key);
      if (cached && cached.expiresAt <= Date.now()) {
        this.state.cache.delete(key);
      }
    }, ttlMs);
  }

  getCache(key) {
    const entry = this.state.cache.get(key);
    
    if (!entry) {
      return null;
    }
    
    if (entry.expiresAt <= Date.now()) {
      this.state.cache.delete(key);
      return null;
    }
    
    return entry.value;
  }

  clearCache() {
    this.state.cache.clear();
  }

  // Settings
  updateSettings(newSettings) {
    this.state.settings = {
      ...this.state.settings,
      ...newSettings
    };
    this.emit('settingsUpdated', this.state.settings);
  }

  getSettings() {
    return { ...this.state.settings };
  }

  // Get full state (for debugging)
  getState() {
    return {
      ...this.state,
      cache: Array.from(this.state.cache.entries()).map(([key, entry]) => ({
        key,
        expiresAt: entry.expiresAt,
        hasValue: !!entry.value
      }))
    };
  }

  // Reset everything
  reset() {
    this.state.canvasSnapshots = [];
    this.state.eventLog = [];
    this.state.cache.clear();
    this.state.connectionStatus = 'disconnected';
    this.state.lastConnectionTime = null;
    this.emit('stateReset');
  }
}

// Singleton instance
let storeInstance = null;

export function getStore() {
  if (!storeInstance) {
    storeInstance = new StateStore();
  }
  return storeInstance;
}

export default StateStore;