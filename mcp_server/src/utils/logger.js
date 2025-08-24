import { getStore } from '../state/store.js';

const LogLevel = {
  QUIET: 0,
  NORMAL: 1,
  VERBOSE: 2
};

class Logger {
  constructor() {
    this.store = getStore();
    this.levelMap = {
      'quiet': LogLevel.QUIET,
      'normal': LogLevel.NORMAL,
      'verbose': LogLevel.VERBOSE
    };
  }

  getLevel() {
    const settings = this.store.getSettings();
    return this.levelMap[settings.logLevel] || LogLevel.NORMAL;
  }

  log(message, level = LogLevel.NORMAL) {
    if (this.getLevel() >= level) {
      console.log(`[${new Date().toISOString()}] ${message}`);
    }
  }

  error(message, error = null) {
    console.error(`[ERROR] ${message}`, error || '');
    this.store.logEvent('ERROR', {
      message,
      error: error?.message || null,
      stack: error?.stack || null
    });
  }

  warn(message) {
    if (this.getLevel() >= LogLevel.NORMAL) {
      console.warn(`[WARN] ${message}`);
    }
  }

  info(message) {
    this.log(message, LogLevel.NORMAL);
  }

  verbose(message) {
    this.log(`[VERBOSE] ${message}`, LogLevel.VERBOSE);
  }

  debug(message, data = null) {
    if (this.getLevel() >= LogLevel.VERBOSE) {
      console.log(`[DEBUG] ${message}`, data || '');
    }
  }

  toolCall(toolName, args = {}) {
    this.info(`Tool called: ${toolName}`);
    this.store.logEvent('MCP_TOOL_CALL', {
      tool: toolName,
      args
    });
  }

  httpRequest(method, path, statusCode = null) {
    const message = statusCode 
      ? `${method} ${path} -> ${statusCode}`
      : `${method} ${path}`;
    this.verbose(message);
  }
}

// Singleton instance
let loggerInstance = null;

export function getLogger() {
  if (!loggerInstance) {
    loggerInstance = new Logger();
  }
  return loggerInstance;
}

export default Logger;