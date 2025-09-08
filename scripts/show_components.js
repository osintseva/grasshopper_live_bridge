// scripts/show_components.js
// Displays the current components from the log file
// Usage: node scripts/show_components.js

const fs = require('fs');
const path = require('path');

const logFile = 'current_gh_components.md';

function showComponents() {
    try {
        if (!fs.existsSync(logFile)) {
            console.log('[!] No component log file found yet.');
            console.log('[i] Create some components first using:');
            console.log('[i]   node scripts/create_python.js scripts/circles.py');
            console.log('[i]   node scripts/create_python.js scripts/extrude_circles.py');
            return;
        }

        const content = fs.readFileSync(logFile, 'utf8');
        
        console.log('=== Current Grasshopper Components ===');
        console.log('');
        
        // Extract component information
        const componentRegex = /## (.+)\n- \*\*ID\*\*: `(.+)`\n- \*\*Type\*\*: (.+)\n- \*\*Created\*\*: (.+)\n- \*\*Position\*\*: \((.+), (.+)\)\n- \*\*Script\*\*: (.+)/g;
        
        let match;
        const components = [];
        
        while ((match = componentRegex.exec(content)) !== null) {
            components.push({
                name: match[1],
                id: match[2], 
                type: match[3],
                created: match[4],
                x: match[5],
                y: match[6],
                script: match[7]
            });
        }
        
        if (components.length === 0) {
            console.log('[!] No components found in log file.');
            console.log('[i] The log might be in a different format.');
            console.log('[i] Raw file content:');
            console.log(content);
            return;
        }
        
        console.log(`Found ${components.length} component(s):`);
        console.log('');
        
        components.forEach((comp, index) => {
            console.log(`${index + 1}. ${comp.name}`);
            console.log(`   ID: ${comp.id}`);
            console.log(`   Position: (${comp.x}, ${comp.y})`);
            console.log(`   Created: ${comp.created}`);
            console.log('');
        });
        
        console.log('=== Connection Examples ===');
        if (components.length >= 2) {
            const comp1 = components[0];
            const comp2 = components[1];
            console.log(`Connect ${comp1.name} to ${comp2.name}:`);
            console.log(`  node scripts/connect_components.js "${comp1.id}" "${comp2.id}" circles circles`);
        }
        
        console.log('');
        console.log('[i] Use these IDs with connect_components.js');
        
    } catch (error) {
        console.error('[!] Error reading component log:', error.message);
    }
}

showComponents();