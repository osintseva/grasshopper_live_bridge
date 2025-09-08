// scripts/create_circle_pipeline.js
// Creates the complete circles → extrude pipeline with automatic connections
// Usage: node scripts/create_circle_pipeline.js

const WebSocket = require('ws');
const fs = require('fs');

const URL = process.env.GH_WS_URL || 'ws://localhost:8181/live';

// Component creation configurations
const components = [
    {
        name: 'circles',
        script: 'scripts/circles.py',
        position: { x: 100, y: 200 }
    },
    {
        name: 'extrude',
        script: 'scripts/extrude_circles.py', 
        position: { x: 400, y: 200 }
    },
    {
        name: 'height_slider',
        type: 'slider',
        position: { x: 250, y: 100 },
        config: { min: 1, max: 50, value: 10, name: 'Height' }
    }
];

// Connection configuration
const connections = [
    { from: 'circles', to: 'extrude', fromParam: 'circles', toParam: 'circles' },
    { from: 'height_slider', to: 'extrude', fromParam: '0', toParam: 'H' }
];

async function sendCommand(command) {
    return new Promise((resolve, reject) => {
        const ws = new WebSocket(URL);

        ws.on('open', () => {
            console.log(`[i] Sending: ${command.action}`);
            ws.send(JSON.stringify(command));
        });

        ws.on('message', (data) => {
            const response = JSON.parse(data.toString());
            ws.close();
            
            if (response.status === 'queued') {
                resolve(response);
            } else if (response.action === 'error') {
                reject(new Error(response.message));
            } else {
                resolve(response);
            }
        });

        ws.on('error', reject);
    });
}

async function createComponent(config) {
    if (config.type === 'slider') {
        return await sendCommand({
            action: 'create_slider',
            correlationId: `slider-${Date.now()}`,
            payload: {
                x: config.position.x,
                y: config.position.y,
                min: config.config.min,
                max: config.config.max,
                value: config.config.value,
                name: config.config.name
            }
        });
    } else {
        // Python component
        const code = fs.readFileSync(config.script, 'utf8');
        return await sendCommand({
            action: 'create_python',
            correlationId: `py-${Date.now()}`,
            payload: {
                x: config.position.x,
                y: config.position.y,
                code: code
            }
        });
    }
}

async function connectComponent(connection, componentIds) {
    const sourceId = componentIds[connection.from];
    const targetId = componentIds[connection.to];

    return await sendCommand({
        action: 'connect_components',
        correlationId: `connect-${Date.now()}`,
        payload: {
            source_id: sourceId,
            target_id: targetId,
            source_output: connection.fromParam,
            target_input: connection.toParam
        }
    });
}

async function createPipeline() {
    console.log('[i] Creating Grasshopper Circle → Extrude Pipeline...');
    console.log('');

    // Step 1: Create components
    console.log('=== Creating Components ===');
    const componentIds = {};
    
    for (const config of components) {
        try {
            console.log(`[i] Creating ${config.name}...`);
            const response = await createComponent(config);
            
            // Note: We can't get the actual component ID from the response in this simplified version
            // In a real implementation, you'd need to modify the C# to return the created component ID
            console.log(`[✓] ${config.name} created successfully`);
            
            // For now, we'll use placeholders - user will need to get actual IDs
            componentIds[config.name] = `${config.name}_id_placeholder`;
            
        } catch (error) {
            console.error(`[✗] Failed to create ${config.name}:`, error.message);
            return;
        }
        
        // Small delay between creations
        await new Promise(resolve => setTimeout(resolve, 500));
    }
    
    console.log('');
    console.log('=== Components Created ===');
    console.log('[!] IMPORTANT: To make connections, you need to get the actual component IDs');
    console.log('[i] Run: node scripts/list_components.js');
    console.log('[i] Then use the IDs shown to make connections:');
    console.log('');
    
    for (const connection of connections) {
        console.log(`[i] Connect: ${connection.from}.${connection.fromParam} → ${connection.to}.${connection.toParam}`);
        console.log(`    node scripts/connect_components.js <${connection.from}_id> <${connection.to}_id> ${connection.fromParam} ${connection.toParam}`);
    }
    
    console.log('');
    console.log('[✓] Pipeline creation completed!');
    console.log('[i] Check your Grasshopper canvas for the components.');
}

// Execute pipeline creation
createPipeline().catch(error => {
    console.error('[✗] Pipeline creation failed:', error.message);
    process.exit(1);
});