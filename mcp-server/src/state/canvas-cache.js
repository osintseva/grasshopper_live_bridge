import { getStore } from './store.js';

export class CanvasCache {
  constructor(ttlMs = 15000) { // 15 seconds default TTL
    this.ttlMs = ttlMs;
    this.store = getStore();
  }

  async getCanvas(forceRefresh = false) {
    const cacheKey = 'canvas-full';
    
    if (!forceRefresh) {
      const cached = this.store.getCache(cacheKey);
      if (cached) {
        return cached;
      }
    }

    // If not cached or force refresh, fetch from Grasshopper
    const { getGrasshopperClient } = await import('../websocket-client.js');
    const client = getGrasshopperClient();
    
    try {
      const canvasData = await client.getCanvasState(true);
      
      // Cache the result
      this.store.setCache(cacheKey, canvasData, this.ttlMs);
      
      // Also store as a snapshot
      this.store.addCanvasSnapshot(canvasData);
      
      return canvasData;
    } catch (error) {
      console.error('[CanvasCache] Failed to fetch canvas:', error);
      
      // Try to return the latest snapshot if available
      const latestSnapshot = this.store.getLatestSnapshot();
      if (latestSnapshot) {
        console.log('[CanvasCache] Returning latest snapshot as fallback');
        return latestSnapshot;
      }
      
      throw error;
    }
  }

  async getSelection(forceRefresh = false) {
    const cacheKey = 'canvas-selection';
    
    if (!forceRefresh) {
      const cached = this.store.getCache(cacheKey);
      if (cached) {
        return cached;
      }
    }

    const { getGrasshopperClient } = await import('../websocket-client.js');
    const client = getGrasshopperClient();
    
    try {
      const selectionData = await client.getSelection();
      
      // Cache with shorter TTL for selection
      this.store.setCache(cacheKey, selectionData, this.ttlMs / 3);
      
      return selectionData;
    } catch (error) {
      console.error('[CanvasCache] Failed to fetch selection:', error);
      throw error;
    }
  }

  async getComponentInfo(componentUuid) {
    const cacheKey = `component-${componentUuid}`;
    
    const cached = this.store.getCache(cacheKey);
    if (cached) {
      return cached;
    }

    // Get full canvas and extract component
    const canvas = await this.getCanvas();
    const component = canvas.Components?.find(c => c.InstanceGuid === componentUuid);
    
    if (component) {
      this.store.setCache(cacheKey, component, this.ttlMs);
    }
    
    return component || null;
  }

  invalidate(key = null) {
    if (key) {
      this.store.state.cache.delete(key);
    } else {
      // Invalidate all canvas-related cache
      for (const [cacheKey] of this.store.state.cache) {
        if (cacheKey.startsWith('canvas-') || cacheKey.startsWith('component-')) {
          this.store.state.cache.delete(cacheKey);
        }
      }
    }
  }

  setTTL(newTtlMs) {
    this.ttlMs = newTtlMs;
  }
}

// Singleton instance
let cacheInstance = null;

export function getCanvasCache(ttlMs) {
  if (!cacheInstance) {
    cacheInstance = new CanvasCache(ttlMs);
  }
  return cacheInstance;
}