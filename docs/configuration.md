# PDK Configuration Guide

## Overview

PDK supports configuration files for persistent settings that control pipeline execution, Docker behavior, logging, and feature flags.

## Configuration File Discovery

PDK searches for configuration files in this order:

1. Path specified by `--config` CLI argument
2. `.pdkrc` in current directory
3. `pdk.config.json` in current directory
4. `~/.pdkrc` in user home directory
5. `~/.pdk/config.json` in user home directory

The first file found is used. If no file is found, PDK uses default values.

## Configuration File Format

Configuration files use JSON format:

```json
{
  "version": "1.0",
  "variables": {
    "BUILD_CONFIG": "Release",
    "NODE_VERSION": "18.x"
  },
  "secrets": {},
  "docker": {
    "defaultRunner": "ubuntu-latest",
    "memoryLimit": "4g",
    "cpuLimit": 2.0,
    "network": "bridge"
  },
  "artifacts": {
    "basePath": ".pdk/artifacts",
    "retentionDays": 7,
    "compression": "gzip"
  },
  "logging": {
    "level": "Info",
    "file": "~/.pdk/logs/pdk.log",
    "maxSizeMb": 10
  },
  "features": {
    "checkUpdates": true,
    "telemetry": false
  }
}
```

## Configuration Sections

### version (required)

Must be `"1.0"`.

### variables

Key-value pairs defining variables for pipeline execution.
Variable names should follow the pattern: `[A-Z_][A-Z0-9_]*`

```json
{
  "variables": {
    "BUILD_CONFIG": "Release",
    "VERSION": "1.2.3"
  }
}
```

### docker

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| defaultRunner | string | "ubuntu-latest" | Default runner image |
| memoryLimit | string | null | Container memory limit (e.g., "4g") |
| cpuLimit | number | null | Container CPU limit (min 0.1) |
| network | string | "bridge" | Docker network to use |

### artifacts

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| basePath | string | ".pdk/artifacts" | Artifact storage path |
| retentionDays | int | 7 | Days to retain artifacts |
| compression | string | "gzip" | Compression algorithm |

### logging

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| level | string | "Info" | Log level: Info, Debug, Warning, Error |
| file | string | null | Log file path (~ supported) |
| maxSizeMb | int | 10 | Max log file size before rotation |

### features

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| checkUpdates | bool | true | Check for PDK updates |
| telemetry | bool | false | Enable telemetry |

## Configuration Merging

When multiple configuration sources exist, they are merged with this precedence (later overrides earlier):

1. Built-in defaults (lowest)
2. Configuration file
3. Environment variables
4. CLI arguments (highest)

Objects are deep-merged; arrays are replaced.

## CLI Usage

```bash
# Use auto-discovery
pdk run --file pipeline.yml

# Use specific config file
pdk run --file pipeline.yml --config ./custom-config.json
```

## Examples

### Minimal Configuration

```json
{
  "version": "1.0",
  "variables": {
    "BUILD_CONFIG": "Release"
  }
}
```

### Team Configuration

```json
{
  "version": "1.0",
  "variables": {
    "DOCKER_REGISTRY": "team.azurecr.io",
    "BUILD_CONFIG": "Release"
  },
  "docker": {
    "defaultRunner": "ubuntu-22.04",
    "memoryLimit": "8g",
    "cpuLimit": 4.0
  },
  "logging": {
    "level": "Info",
    "maxSizeMb": 50
  }
}
```

### CI/CD Configuration

```json
{
  "version": "1.0",
  "variables": {
    "BUILD_CONFIG": "Release",
    "DOCKER_REGISTRY": "ghcr.io/myorg"
  },
  "docker": {
    "memoryLimit": "8g",
    "cpuLimit": 4.0
  },
  "logging": {
    "level": "Debug"
  }
}
```

### Development Configuration

```json
{
  "version": "1.0",
  "variables": {
    "BUILD_CONFIG": "Debug",
    "SKIP_TESTS": "false"
  },
  "features": {
    "checkUpdates": false
  }
}
```

## Troubleshooting

### Configuration Not Found

If PDK cannot find your configuration file:
1. Verify the file exists in one of the search locations
2. Check file permissions
3. Use `--config` to specify the exact path

### Invalid JSON

If you see a parse error:
1. Validate your JSON using a linter
2. Ensure all strings are quoted
3. Check for trailing commas (not allowed in standard JSON)

### Variables Not Expanding

If variables aren't being replaced:
1. Verify the syntax: `${VARIABLE_NAME}`
2. Check that the variable is defined in the config
3. Variables are case-sensitive
