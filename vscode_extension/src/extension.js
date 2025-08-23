const vscode = require('vscode');
const WebSocket = require('ws');
const path = require('path');

let ws = null;
let statusBarItem = null;
let linkedFiles = new Map(); // Map of file paths to GH component IDs

function activate(context) {
    console.log('Grasshopper Live Coding extension activated');

    // Create status bar item
    statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
    statusBarItem.text = "$(circle-slash) GH Disconnected";
    statusBarItem.tooltip = "Click to connect to Grasshopper";
    statusBarItem.command = 'grasshopper.connect';
    statusBarItem.show();
    context.subscriptions.push(statusBarItem);

    // Register commands
    context.subscriptions.push(
        vscode.commands.registerCommand('grasshopper.connect', connectToGrasshopper),
        vscode.commands.registerCommand('grasshopper.disconnect', disconnect),
        vscode.commands.registerCommand('grasshopper.linkFile', linkCurrentFile),
        vscode.commands.registerCommand('grasshopper.unlinkFile', unlinkCurrentFile),
        vscode.commands.registerCommand('grasshopper.testSlider', testCreateSlider)
    );

    // Auto-connect on activation
    connectToGrasshopper();

    // Watch for file changes (with debouncing)
    let changeTimeout = null;
    context.subscriptions.push(
        vscode.workspace.onDidChangeTextDocument(event => {
            const filePath = event.document.fileName;

            // Only process .py files that are linked
            if (!filePath.endsWith('.py') || !linkedFiles.has(filePath)) {
                return;
            }

            // Debounce - wait 500ms after user stops typing
            if (changeTimeout) {
                clearTimeout(changeTimeout);
            }

            changeTimeout = setTimeout(() => {
                sendFileUpdate(event.document);
            }, 500);
        })
    );

    // Also update on save (immediate, no debounce)
    context.subscriptions.push(
        vscode.workspace.onDidSaveTextDocument(document => {
            const filePath = document.fileName;
            if (filePath.endsWith('.py') && linkedFiles.has(filePath)) {
                sendFileUpdate(document);
            }
        })
    );
}

function connectToGrasshopper() {
    if (ws && ws.readyState === WebSocket.OPEN) {
        vscode.window.showInformationMessage('Already connected to Grasshopper');
        return;
    }

    ws = new WebSocket('ws://localhost:8181/live');

    ws.on('open', () => {
        statusBarItem.text = "$(check) GH Connected";
        statusBarItem.tooltip = "Connected to Grasshopper on port 8181";
        vscode.window.showInformationMessage('Connected to Grasshopper! 🚀');
    });

    ws.on('message', (data) => {
        const response = JSON.parse(data.toString());
        console.log('Response from GH:', response);

        if (response.status === 'error') {
            vscode.window.showErrorMessage(`Grasshopper error: ${response.message || 'Unknown error'}`);
        }
    });

    ws.on('error', (error) => {
        statusBarItem.text = "$(error) GH Error";
        vscode.window.showErrorMessage(`WebSocket error: ${error.message}`);
    });

    ws.on('close', () => {
        statusBarItem.text = "$(circle-slash) GH Disconnected";
        statusBarItem.tooltip = "Click to reconnect to Grasshopper";

        // Auto-reconnect after 5 seconds
        setTimeout(() => {
            if (!ws || ws.readyState !== WebSocket.OPEN) {
                connectToGrasshopper();
            }
        }, 5000);
    });
}

function disconnect() {
    if (ws) {
        ws.close();
        ws = null;
        vscode.window.showInformationMessage('Disconnected from Grasshopper');
    }
}

async function linkCurrentFile() {
    const editor = vscode.window.activeTextEditor;
    if (!editor) {
        vscode.window.showErrorMessage('No active file to link');
        return;
    }

    const filePath = editor.document.fileName;
    if (!filePath.endsWith('.py')) {
        vscode.window.showErrorMessage('Only Python files can be linked');
        return;
    }

    // Prompt for component ID
    const componentId = await vscode.window.showInputBox({
        prompt: 'Enter Grasshopper component ID (GUID)',
        placeHolder: 'Paste the component GUID from Grasshopper',
        value: await vscode.env.clipboard.readText() // Try to read from clipboard
    });

    if (componentId) {
        linkedFiles.set(filePath, componentId);
        vscode.window.showInformationMessage(
            `Linked ${path.basename(filePath)} to GH component ${componentId.substring(0, 8)}...`
        );

        // Send initial content
        sendFileUpdate(editor.document);
    }
}

function unlinkCurrentFile() {
    const editor = vscode.window.activeTextEditor;
    if (!editor) return;

    const filePath = editor.document.fileName;
    if (linkedFiles.delete(filePath)) {
        vscode.window.showInformationMessage(`Unlinked ${path.basename(filePath)}`);
    }
}

function sendFileUpdate(document) {
    if (!ws || ws.readyState !== WebSocket.OPEN) {
        vscode.window.showWarningMessage('Not connected to Grasshopper');
        return;
    }

    const filePath = document.fileName;
    const componentId = linkedFiles.get(filePath);

    if (!componentId) {
        return; // File not linked
    }

    const message = {
        action: 'update_script',
        correlationId: Date.now().toString(),
        payload: {
            componentId: componentId,
            code: document.getText()
        }
    };

    ws.send(JSON.stringify(message));

    // Show subtle notification in status bar
    const originalText = statusBarItem.text;
    statusBarItem.text = "$(sync~spin) Updating...";
    setTimeout(() => {
        statusBarItem.text = originalText;
    }, 500);
}

function testCreateSlider() {
    if (!ws || ws.readyState !== WebSocket.OPEN) {
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

    ws.send(JSON.stringify(message));
    vscode.window.showInformationMessage('Created test slider in Grasshopper');
}

function deactivate() {
    if (ws) {
        ws.close();
    }
    if (statusBarItem) {
        statusBarItem.dispose();
    }
}

module.exports = {
    activate,
    deactivate
};