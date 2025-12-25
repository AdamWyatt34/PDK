const { describe, it } = require('node:test');
const assert = require('node:assert');
const { add, subtract, multiply, divide } = require('../src/index');

describe('Calculator', () => {
  describe('add', () => {
    it('should add two positive numbers', () => {
      assert.strictEqual(add(2, 3), 5);
    });

    it('should add negative numbers', () => {
      assert.strictEqual(add(-1, -2), -3);
    });

    it('should add zero', () => {
      assert.strictEqual(add(5, 0), 5);
    });
  });

  describe('subtract', () => {
    it('should subtract two numbers', () => {
      assert.strictEqual(subtract(10, 4), 6);
    });

    it('should handle negative result', () => {
      assert.strictEqual(subtract(3, 7), -4);
    });
  });

  describe('multiply', () => {
    it('should multiply two numbers', () => {
      assert.strictEqual(multiply(6, 7), 42);
    });

    it('should multiply by zero', () => {
      assert.strictEqual(multiply(5, 0), 0);
    });

    it('should multiply negative numbers', () => {
      assert.strictEqual(multiply(-3, 4), -12);
    });
  });

  describe('divide', () => {
    it('should divide two numbers', () => {
      assert.strictEqual(divide(20, 4), 5);
    });

    it('should handle decimal results', () => {
      assert.strictEqual(divide(7, 2), 3.5);
    });

    it('should throw on division by zero', () => {
      assert.throws(() => divide(10, 0), /Cannot divide by zero/);
    });
  });
});
