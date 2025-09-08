// scripts/connect_components.js
// Elegant API for connecting Grasshopper components
// Usage: node scripts/connect_components.js <source_id> <target_id> [source_output] [target_input]
// Example: node scripts/connect_components.js abc123 def456 circles circles

const WebSocket = require('ws');

const URL = process.env.GH_WS_URL || 'ws://localhost:8181/live';

// Parse arguments
const args = process.argv.slice(2);
if (args.length < 2) {
    console.error('Usage: node scripts/connect_components.js <source_id> <target_id> [source_output] [target_input]');
    console.error('');
    console.error('Examples:');
    console.error('  node scripts/connect_components.js abc123 def456                    # Connect output[0] → input[0]');
    console.error('  node scripts/connect_components.js abc123 def456 circles circles  # Connect by parameter name');
    console.error('  node scripts/connect_components.js abc123 def456 0 1              # Connect by index');
    console.error('');
    console.error('To get component IDs, check the Grasshopper component properties or use list_components.js');
    process.exit(1);
}

const sourceId = args[0];
const targetId = args[1];
const sourceOutput = args[2] || '0';  // Default to first output
const targetInput = args[3] || '0';   // Default to first input

function connectComponents(source_id, target_id, source_output, target_input) {
    return new Promise((resolve, reject) => {
        const ws = new WebSocket(URL);

        ws.on('open', () => {
            console.log('[i] Connected to Grasshopper Live Coding server.');
            console.log(`[i] Connecting: ${source_id}.${source_output} → ${target_id}.${target_input}`);

            const payload = {
                action: 'connect_components',
                correlationId: `connect-${Date.now()}`,
                payload: {
                    source_id: source_id,
                    target_id: target_id,
                    source_output: source_output,
                    target_input: target_input
                }
            };

            ws.send(JSON.stringify(payload));
        });

        ws.on('message', (data) => {
            const response = JSON.parse(data.toString());
            
            if (response.status === 'queued') {
                console.log('[✓] Connection command queued successfully');
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

// Execute connection
connectComponents(sourceId, targetId, sourceOutput, targetInput)
    .then(() => {
        console.log('[✓] Connection completed successfully!');
        console.log('[i] Check the Grasshopper canvas to see the connection.');
    })
    .catch((error) => {
        console.error('[✗] Connection failed:', error.message);
        process.exit(1);
    });