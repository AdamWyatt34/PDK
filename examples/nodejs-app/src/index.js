/**
 * Simple calculator module for PDK example
 */

function add(a, b) {
  return a + b;
}

function subtract(a, b) {
  return a - b;
}

function multiply(a, b) {
  return a * b;
}

function divide(a, b) {
  if (b === 0) {
    throw new Error('Cannot divide by zero');
  }
  return a / b;
}

// Export functions
module.exports = {
  add,
  subtract,
  multiply,
  divide
};

// If run directly, demonstrate usage
if (require.main === module) {
  console.log('Calculator Demo');
  console.log('================');
  console.log(`5 + 3 = ${add(5, 3)}`);
  console.log(`10 - 4 = ${subtract(10, 4)}`);
  console.log(`6 * 7 = ${multiply(6, 7)}`);
  console.log(`20 / 4 = ${divide(20, 4)}`);
}
