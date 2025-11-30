/**
 * Simple Node.js application for integration testing PDK executors.
 */

console.log('Hello from PDK test project!');
console.log(`Running on Node.js ${process.version}`);

if (process.argv.length > 2) {
    console.log(`Arguments: ${process.argv.slice(2).join(', ')}`);
}
