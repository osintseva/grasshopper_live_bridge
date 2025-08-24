import WebSocket from 'ws';
import { EventEmitter } from 'events';

export class GrasshopperClient extends EventEmitter {
  constructor(config = {}) {
    super();
    this.host = config.host || '127.0.0.1';
    this.port = config.port || 8181;
    this.url = `ws://${this.host}:${this.port}/live`;
    this.reconnectInterval = config.reconnectInterval || 5000;
    this.timeout = config.timeout || 10000;
    
    this.ws = null;
    this.isConnected = false;
    this.pendingRequests = new Map();
    this.shouldReconnect = false;
  }

  connect() {
    return new Promise((resolve, reject) => {
      if (this.isConnected) {
        resolve();
        return;
      }

      console.log(`[GH Client] Connecting to ${this.url}...`);
      this.ws = new WebSocket(this.url);
      
      const connectionTimeout = setTimeout(() => {
        this.ws.close();
        reject(new Error('Connection timeout'));
      }, this.timeout);

      this.ws.on('open', () => {
        clearTimeout(connectionTimeout);
        this.isConnected = true;
        this.shouldReconnect = true;
        console.log('[GH Client] Connected to Grasshopper');
        this.emit('connected');
        resolve();
      });

      this.ws.on('message', (data) => {
        try {
          const message = JSON.parse(data.toString());
          this.handleMessage(message);
        } catch (err) {
          console.error('[GH Client] Failed to parse message:', err);
        }
      });

      this.ws.on('error', (err) => {
        clearTimeout(connectionTimeout);
        console.error('[GH Client] WebSocket error:', err.message);
        this.emit('error', err);
        if (!this.isConnected) {
          reject(err);
        }
      });

      this.ws.on('close', () => {
        this.isConnected = false;
        this.emit('disconnected');
        console.log('[GH Client] Disconnected from Grasshopper');
        
        // Clear pending requests
        for (const [, reject] of this.pendingRequests) {
          reject(new Error('Connection closed'));
        }
        this.pendingRequests.clear();

        // Auto-reconnect if needed
        if (this.shouldReconnect) {
          setTimeout(() => {
            if (this.shouldReconnect) {
              this.connect().catch(console.error);
            }
          }, this.reconnectInterval);
        }
      });
    });
  }

  disconnect() {
    this.shouldReconnect = false;
    if (this.ws) {
      this.ws.close();
      this.ws = null;
    }
  }

  handleMessage(message) {
    // Handle responses to requests
    if (message.correlationId && this.pendingRequests.has(message.correlationId)) {
      const { resolve, reject } = this.pendingRequests.get(message.correlationId);
      this.pendingRequests.delete(message.correlationId);
      
      if (message.status === 'success' || message.ok) {
        resolve(message);
      } else {
        reject(new Error(message.error?.message || 'Request failed'));
      }
      return;
    }

    // Handle push events from Grasshopper
    if (message.type === 'event') {
      this.emit(message.event, message.data);
      this.emit('event', message);
    }
  }

  async send(action, data = {}) {
    if (!this.isConnected) {
      await this.connect();
    }

    return new Promise((resolve, reject) => {
      const correlationId = `mcp-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
      
      const request = {
        action,
        correlationId,
        ts: Date.now(),
        data
      };

      const timeout = setTimeout(() => {
        this.pendingRequests.delete(correlationId);
        reject(new Error(`Request timeout: ${action}`));
      }, this.timeout);

      this.pendingRequests.set(correlationId, {
        resolve: (response) => {
          clearTimeout(timeout);
          resolve(response);
        },
        reject: (error) => {
          clearTimeout(timeout);
          reject(error);
        }
      });

      this.ws.send(JSON.stringify(request));
    });
  }

  async getCanvasState(includeSelection = false) {
    const response = await this.send('get_canvas_info', { includeSelection });
    
    // Parse the data if it's a string
    if (response.data && typeof response.data === 'string') {
      try {
        return JSON.parse(response.data);
      } catch {
        return response.data;
      }
    }
    
    return response.data || response;
  }

  async getSelection() {
    const response = await this.send('get_selection');
    return response.data || response;
  }

  async pushScriptUpdate(componentUuid, filePath, language = 'python', sha256 = null) {
    const response = await this.send('script_updated', {
      componentUuid,
      filePath,
      language,
      sha256,
      fileSize: 0, // Will be calculated by Grasshopper
      idempotencyKey: `${componentUuid}-${Date.now()}`
    });
    return response.data || response;
  }

  subscribe(events = []) {
    return this.send('hello', {
      subscribe: events,
      client: 'mcp-server'
    });
  }
}

// Singleton instance
let clientInstance = null;

export function getGrasshopperClient(config) {
  if (!clientInstance) {
    clientInstance = new GrasshopperClient(config);
  }
  return clientInstance;
}