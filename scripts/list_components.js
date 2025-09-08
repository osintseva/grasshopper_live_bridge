// scripts/list_components.js
// Lists all components in the current Grasshopper document with their IDs and parameters
// Usage: node scripts/list_components.js

const WebSocket = require('ws');

const URL = process.env.GH_WS_URL || 'ws://localhost:8181/live';

function listComponents() {
    return new Promise((resolve, reject) => {
        const ws = new WebSocket(URL);

        ws.on('open', () => {
            console.log('[i] Connected to Grasshopper Live Coding server.');
            console.log('[i] Requesting component list...');

            const payload = {
                action: 'list_components',
                correlationId: `list-${Date.now()}`,
                payload: {}
            };

            ws.send(JSON.stringify(payload));
        });

        ws.on('message', (data) => {
            const response = JSON.parse(data.toString());
            
            console.log('[i] Raw response:', response);
            
            if (response.status === 'queued') {
                console.log('[✓] Component list request queued successfully');
                console.log('[i] Check the Grasshopper Live Coding Controller component messages for the component list.');
                
                // Wait a bit and suggest checking the component
                setTimeout(() => {
                    console.log('');
                    console.log('[!] If you don\'t see the component list:');
                    console.log('[i] 1. Check the Live Coding Controller component on your canvas');
                    console.log('[i] 2. Right-click it → "Show errors and warnings"');
                    console.log('[i] 3. Look for "=== Grasshopper Components ===" in the messages');
                    console.log('[i] 4. If still no messages, the component may not be running properly');
                }, 1000);
                
                resolve(response);
            } else if (response.action === 'error') {
                console.error('[✗] Error:', response.message);
                reject(new Error(response.message));
            } else {
                console.log('[i] Response:', response);
                resolve(response);
            }
            
            ws.close();
        });

        ws.on('error', (err) => {
            console.error('[!] WebSocket error:', err.message);
            reject(err);
        });

        ws.on('close', () => {
            console.log('[i] Disconnected from server.');
        });
    });
}

// Execute listing
listComponents()
    .then(() => {
        console.log('');
        console.log('[i] The component list has been displayed in the Grasshopper Live Coding Controller messages.');
        console.log('[i] Look for messages starting with "=== Grasshopper Components ==="');
        console.log('[i] Use the IDs shown there with connect_components.js');
        console.log('');
        console.log('[i] Example connection:');
        console.log('[i]   node scripts/connect_components.js <circles_component_id> <extrude_component_id> circles circles');
    })
    .catch((error) => {
        console.error('[✗] List components failed:', error.message);
        process.exit(1);
    });