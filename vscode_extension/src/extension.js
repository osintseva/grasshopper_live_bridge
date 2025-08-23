// vscode-extension/src/extension.js
const vscode = require('vscode');
const WebSocket = require('ws');
const path = require('path');

let ws = null;
let isConnecting = false;
let reconnectTimer = null;

let statusBarItem = null;
let linkedFiles = new Map(); // Map<absoluteFilePath, componentId>

const output = vscode.window.createOutputChannel('Grasshopper Live');

// ----- status bar helpers (stable state + reference-counted spinner) -----
let stableStatus = { text: '$(circle-slash) GH Disconnected', tooltip: 'Click to connect to Grasshopper', command: 'grasshopper.connect' };
let spinnerDepth = 0;

function applyStatus({ text, tooltip, command }) {
  if (!statusBarItem) return;
  statusBarItem.text = text;
  statusBarItem.tooltip = tooltip || '';
  statusBarItem.command = command || undefined;
  statusBarItem.show();
}

function setStableStatus(text, tooltip, command) {
  stableStatus = { text, tooltip, command };
  if (spinnerDepth === 0) {
    applyStatus(stableStatus);
  }
}

function startSpinner(label = '$(sync~spin) Updating…', tooltip = 'Sending code to Grasshopper') {
  spinnerDepth += 1;
  applyStatus({ text: label, tooltip, command: undefined });
}

function stopSpinner() {
  spinnerDepth = Math.max(0, spinnerDepth - 1);
  if (spinnerDepth === 0) {
    applyStatus(stableStatus);
  }
}

// ------------------------------------------------------------------------

function activate(context) {
  output.appendLine('Grasshopper Live Coding extension activated');

  statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
  setStableStatus('$(circle-slash) GH Disconnected', 'Click to connect to Grasshopper', 'grasshopper.connect');
  context.subscriptions.push(statusBarItem);

  context.subscriptions.push(
    vscode.commands.registerCommand('grasshopper.connect', connectToGrasshopper),
    vscode.commands.registerCommand('grasshopper.disconnect', disconnect),
    vscode.commands.registerCommand('grasshopper.linkFile', linkCurrentFile),
    vscode.commands.registerCommand('grasshopper.unlinkFile', unlinkCurrentFile),
    vscode.commands.registerCommand('grasshopper.testSlider', testCreateSlider)
  );

  // Auto-connect for POC convenience
  connectToGrasshopper();

  // Debounced live updates while typing
  let changeTimeout = null;
  context.subscriptions.push(
    vscode.workspace.onDidChangeTextDocument(event => {
      const filePath = event.document.fileName;
      if (!filePath.toLowerCase().endsWith('.py')) return;
      if (!linkedFiles.has(filePath)) return;

      if (changeTimeout) clearTimeout(changeTimeout);
      changeTimeout = setTimeout(() => {
        sendFileUpdate(event.document, /*notifyWhenDisconnected*/ false);
      }, 500);
    })
  );

  // Immediate update on save
  context.subscriptions.push(
    vscode.workspace.onDidSaveTextDocument(document => {
      const filePath = document.fileName;
      if (!filePath.toLowerCase().endsWith('.py')) return;
      if (!linkedFiles.has(filePath)) return;
      sendFileUpdate(document, /*notifyWhenDisconnected*/ false);
    })
  );
}

function connectToGrasshopper() {
  if (ws && ws.readyState === WebSocket.OPEN) {
    vscode.window.showInformationMessage('Already connected to Grasshopper');
    return;
  }
  if (isConnecting) {
    vscode.window.showInformationMessage('Connecting to Grasshopper…');
    return;
  }

  if (reconnectTimer) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }

  isConnecting = true;
  setStableStatus('$(sync~spin) GH Connecting…', 'Connecting to Grasshopper on ws://localhost:8181/live');

  const socket = new WebSocket('ws://localhost:8181/live');
  ws = socket;

  socket.on('open', () => {
    if (ws !== socket) return;
    isConnecting = false;
    setStableStatus('$(check) GH Connected', 'Connected to Grasshopper on port 8181', 'grasshopper.disconnect');
    output.appendLine('[WS] Connected');
    vscode.window.showInformationMessage('Connected to Grasshopper! 🚀');
  });

  socket.on('message', (data) => {
    if (ws !== socket) return;
    try {
      const response = JSON.parse(data.toString());
      output.appendLine(`[WS] Message: ${JSON.stringify(response)}`);
      if (response.status === 'error') {
        vscode.window.showErrorMessage(`Grasshopper error: ${response.message || 'Unknown error'}`);
      }
    } catch (err) {
      output.appendLine(`[WS] Non-JSON message: ${String(data)}`);
    }
  });

  socket.on('error', (error) => {
    if (ws !== socket) return;
    isConnecting = false;
    setStableStatus('$(error) GH Error', `WebSocket error: ${error.message}`, 'grasshopper.connect');
    output.appendLine(`[WS] Error: ${error.message}`);
    vscode.window.showErrorMessage(`WebSocket error: ${error.message}`);
  });

  socket.on('close', () => {
    if (ws !== socket) return;
    isConnecting = false;
    setStableStatus('$(circle-slash) GH Disconnected', 'Click to reconnect to Grasshopper', 'grasshopper.connect');
    output.appendLine('[WS] Closed');

    reconnectTimer = setTimeout(() => {
      if (!ws || ws.readyState !== WebSocket.OPEN) {
        connectToGrasshopper();
      }
    }, 5000);
  });
}

function disconnect() {
  if (reconnectTimer) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }
  if (ws) {
    try { ws.close(); } catch (_) {}
  }
  ws = null;
  isConnecting = false;
  setStableStatus('$(circle-slash) GH Disconnected', 'Click to connect to Grasshopper', 'grasshopper.connect');
  vscode.window.showInformationMessage('Disconnected from Grasshopper');
}

/** Wait until socket is OPEN. Tries to connect if needed. */
function ensureConnected(timeoutMs = 5000) {
  return new Promise((resolve) => {
    if (ws && ws.readyState === WebSocket.OPEN) return resolve(true);
    if (!ws || ws.readyState === WebSocket.CLOSED) {
      connectToGrasshopper();
    }

    const start = Date.now();
    const timer = setInterval(() => {
      if (ws && ws.readyState === WebSocket.OPEN) {
        clearInterval(timer);
        resolve(true);
      } else if (Date.now() - start > timeoutMs) {
        clearInterval(timer);
        resolve(false);
      }
    }, 100);
  });
}

async function linkCurrentFile() {
  const editor = vscode.window.activeTextEditor;
  if (!editor) {
    vscode.window.showErrorMessage('No active file to link');
    return;
  }

  const filePath = editor.document.fileName;
  if (!filePath.toLowerCase().endsWith('.py')) {
    vscode.window.showErrorMessage('Only Python files can be linked');
    return;
  }

  const componentId = await vscode.window.showInputBox({
    prompt: 'Enter Grasshopper component ID (GUID)',
    placeHolder: 'Paste the component GUID from Grasshopper',
    value: await vscode.env.clipboard.readText()
  });

  if (!componentId) return;

  linkedFiles.set(filePath, componentId);
  vscode.window.showInformationMessage(
    `Linked ${path.basename(filePath)} to GH component ${componentId.substring(0, 8)}…`
  );

  await sendFileUpdate(editor.document, /*notifyWhenDisconnected*/ true);
}

function unlinkCurrentFile() {
  const editor = vscode.window.activeTextEditor;
  if (!editor) return;
  const filePath = editor.document.fileName;
  if (linkedFiles.delete(filePath)) {
    vscode.window.showInformationMessage(`Unlinked ${path.basename(filePath)}`);
  }
}

async function sendFileUpdate(document, notifyWhenDisconnected = false) {
  const filePath = document.fileName;
  const componentId = linkedFiles.get(filePath);
  if (!componentId) return;

  const ok = await ensureConnected(4000);
  if (!ok || !ws || ws.readyState !== WebSocket.OPEN) {
    if (notifyWhenDisconnected) {
      vscode.window.showWarningMessage('Not connected to Grasshopper');
    }
    return;
  }

  const message = {
    action: 'update_script',
    correlationId: Date.now().toString(),
    payload: { componentId, code: document.getText() }
  };

  try {
    startSpinner();
    ws.send(JSON.stringify(message));
  } catch (err) {
    output.appendLine(`[WS] send failed: ${err.message}`);
    vscode.window.showErrorMessage(`Failed to send update: ${err.message}`);
  } finally {
    setTimeout(stopSpinner, 500);
  }
}

async function testCreateSlider() {
  const ok = await ensureConnected(4000);
  if (!ok) {
    vscode.window.showErrorMessage('Not connected to Grasshopper');
    return;
  }

  const message = {
    action: 'create_slider',
    correlationId: Date.now().toString(),
    payload: {
      x: Math.random() * 500 + 100,
      y: Math.random() * 500 + 100,
      nickname: 'Test from VSCode'
    }
  };

  try {
    ws.send(JSON.stringify(message));
    vscode.window.showInformationMessage('Created test slider in Grasshopper');
  } catch (err) {
    output.appendLine(`[WS] send failed: ${err.message}`);
    vscode.window.showErrorMessage(`Failed to send: ${err.message}`);
  }
}

function deactivate() {
  if (reconnectTimer) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }
  if (ws) {
    try { ws.close(); } catch (_) {}
    ws = null;
  }
  if (statusBarItem) statusBarItem.dispose();
  output.dispose();
}

module.exports = { activate, deactivate };
