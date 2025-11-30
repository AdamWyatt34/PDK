/**
 * Simple test for integration testing PDK executors.
 */

console.log('Running tests...');
console.log('');

// Test 1: Basic arithmetic
console.log('Test 1: Basic arithmetic');
if (1 + 1 === 2) {
    console.log('✓ Test passed: 1 + 1 = 2');
} else {
    console.log('✗ Test failed: 1 + 1 != 2');
    process.exit(1);
}

// Test 2: String concatenation
console.log('Test 2: String concatenation');
if ('Hello' + ' ' + 'World' === 'Hello World') {
    console.log('✓ Test passed: String concatenation works');
} else {
    console.log('✗ Test failed: String concatenation failed');
    process.exit(1);
}

console.log('');
console.log('All tests passed!');
process.exit(0);
