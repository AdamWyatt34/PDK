# Node.js Application Example

A simple Node.js application demonstrating a CI pipeline with PDK.

## Quick Start

```bash
# Run the pipeline
pdk run

# Run specific steps
pdk run --step-filter "Build" --step-filter "Test"

# Watch mode for development
pdk run --watch --step-filter "Build"
```

## Project Structure

```
nodejs-app/
├── .github/
│   └── workflows/
│       └── ci.yml          # GitHub Actions workflow
├── src/
│   └── index.js            # Main application
├── tests/
│   └── index.test.js       # Unit tests
├── package.json            # Project configuration
└── README.md
```

## Pipeline Stages

```mermaid
graph LR
    A[Checkout] --> B[Setup Node]
    B --> C[Install]
    C --> D[Lint]
    D --> E[Test]
    E --> F[Build]
```

## Running the Example

### Full Pipeline

```bash
cd examples/nodejs-app
pdk run --file .github/workflows/ci.yml
```

### Development Mode

```bash
# Watch for changes and rebuild
pdk run --watch --step-filter "Build"

# Skip linting for faster iteration
pdk run --skip-step "Lint"
```

### Validation

```bash
# Dry-run to validate pipeline
pdk run --dry-run --verbose
```

## Requirements

- Node.js 18+
- Docker (optional, for container execution)
- PDK installed

## Application

The application provides a simple calculator module with:

- `add(a, b)` - Add two numbers
- `subtract(a, b)` - Subtract two numbers
- `multiply(a, b)` - Multiply two numbers
- `divide(a, b)` - Divide two numbers

## See Also

- [Node.js Example Docs](../../docs/examples/nodejs-app.md)
- [Getting Started Guide](../../docs/getting-started.md)
