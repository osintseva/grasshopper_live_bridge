// Test where the C# component tries to save the log file
const path = require('path');
const fs = require('fs');

console.log('Current working directory:', process.cwd());
console.log('Expected log file location:', path.join(process.cwd(), 'current_gh_components.md'));

// Check if file exists
const logPath = 'current_gh_components.md';
if (fs.existsSync(logPath)) {
    console.log('✓ Log file exists!');
    console.log('Content preview:');
    console.log(fs.readFileSync(logPath, 'utf8').substring(0, 200) + '...');
} else {
    console.log('✗ Log file does not exist yet');
    console.log('Make sure you:');
    console.log('1. Rebuilt the C# component');
    console.log('2. Installed the new .gha file');  
    console.log('3. Created a Python component');
}