# PDK Sprint 7: Configuration & Variables
## Requirements Document

**Document Version:** 1.0  
**Status:** Ready for Implementation  
**Sprint:** 7  
**Author:** PDK Development Team  
**Last Updated:** 2024-11-30  

---

## Executive Summary

Sprint 7 adds powerful configuration and variable management capabilities to PDK, enabling users to customize pipeline execution through configuration files, environment variables, and secrets. This sprint delivers a flexible, secure system for managing variables and secrets with proper precedence rules and masking.

### Goals
- Support configuration files for persistent settings
- Enable variable resolution with clear precedence rules
- Implement secure secret management with output masking
- Provide flexible variable interpolation syntax
- Integrate seamlessly with existing pipeline execution

### Success Criteria
- Users can define variables in configuration files
- Environment variables override configuration values
- CLI arguments override both environment and configuration
- Secrets are never exposed in logs or output
- Variable interpolation works in pipeline definitions
- All features have 80%+ test coverage

---

## Feature Requirements

### FR-07-001: Configuration File Support
**Priority:** High  
**Complexity:** Medium  
**Dependencies:** None

#### Description
Implement support for configuration files (`.pdkrc` or `pdk.config.json`) that allow users to define persistent settings for PDK, including variables, Docker configuration, and artifact paths.

#### Requirements

**REQ-07-001: Configuration File Discovery**
- Search for configuration files in this order:
  1. Path specified by `--config` CLI argument
  2. `.pdkrc` in current directory
  3. `pdk.config.json` in current directory
  4. `.pdkrc` in user home directory (`~/.pdkrc`)
  5. `pdk.config.json` in user home directory (`~/.pdk/config.json`)
- Use first file found
- If no file found, use default configuration
- Log which configuration file is loaded (at DEBUG level)

**REQ-07-002: Configuration File Format**
Support JSON format with this schema:
```json
{
  "version": "1.0",
  "variables": {
    "BUILD_CONFIG": "Release",
    "NODE_VERSION": "18.x",
    "DOCKER_REGISTRY": "myregistry.azurecr.io"
  },
  "secrets": {
    "API_KEY": "encrypted:base64encodedvalue",
    "DB_PASSWORD": "encrypted:base64encodedvalue"
  },
  "docker": {
    "defaultRunner": "ubuntu-latest",
    "memoryLimit": "4g",
    "cpuLimit": "2.0",
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

**REQ-07-003: Configuration Schema Validation**
- Validate configuration file against JSON schema
- Required fields:
  - `version`: Must be "1.0"
- Optional fields with defaults:
  - `variables`: Empty object
  - `secrets`: Empty object
  - `docker`: Default Docker settings
  - `artifacts`: Default artifact settings
  - `logging`: Default logging settings
  - `features`: Default feature flags
- Reject configuration with invalid schema
- Provide helpful error messages for validation failures

**REQ-07-004: Configuration Merging**
- Default configuration (built-in defaults)
- User configuration file (lowest precedence)
- Environment variables (medium precedence)
- CLI arguments (highest precedence)
- Merge order: defaults → file → environment → CLI
- Later sources override earlier sources
- Arrays are replaced (not merged)
- Objects are deep-merged

**REQ-07-005: Configuration Access**
- Provide `IConfiguration` interface for accessing configuration
- Thread-safe access to configuration values
- Support typed access: `GetString()`, `GetInt()`, `GetBool()`
- Support nested key access: `docker.memoryLimit`
- Return null for missing keys (or throw if required)
- Cache parsed configuration (don't re-parse on every access)

#### Acceptance Criteria
- ✅ Configuration files discovered in correct order
- ✅ JSON configuration parsed correctly
- ✅ Schema validation catches invalid configurations
- ✅ Configuration merging follows precedence rules
- ✅ IConfiguration interface provides type-safe access
- ✅ Helpful error messages for configuration errors
- ✅ Unit tests cover parsing and validation
- ✅ Integration tests verify file discovery

---

### FR-07-002: Variable Resolution System
**Priority:** High  
**Complexity:** Medium  
**Dependencies:** FR-07-001 (Configuration File Support)

#### Description
Implement a comprehensive variable resolution system that resolves variables from multiple sources (CLI, environment, configuration) with clear precedence rules and supports interpolation syntax.

#### Requirements

**REQ-07-010: Variable Sources**
Support variables from these sources (highest to lowest precedence):
1. **CLI arguments**: `--var KEY=VALUE` or `--var KEY=$VALUE`
2. **Environment variables**: Standard environment variables
3. **Configuration file**: `variables` section in config
4. **Built-in variables**: System-defined variables (see REQ-07-014)

**REQ-07-011: Variable Resolution**
- When variable requested, check sources in precedence order
- Return first value found
- Return null if variable not found in any source
- Case-sensitive variable names (follow environment variable conventions)
- Support empty string as valid value (different from null)

**REQ-07-012: Variable Interpolation Syntax**
Support interpolation in pipeline files and configuration:
- Syntax: `${VARIABLE_NAME}` or `$VARIABLE_NAME`
- Recursive resolution: `${PATH}/${SUBPATH}`
- Default values: `${VAR_NAME:-default_value}`
- Required variables: `${VAR_NAME:?error message if missing}`
- Escape syntax: `\${NOT_A_VARIABLE}` → `${NOT_A_VARIABLE}`

**Examples:**
```yaml
# In pipeline YAML
steps:
  - name: Build
    run: dotnet build --configuration ${BUILD_CONFIG}
  
  - name: Deploy
    run: |
      docker push ${DOCKER_REGISTRY:-localhost:5000}/myapp:${VERSION}
  
  - name: Required var example
    run: echo ${API_ENDPOINT:?API_ENDPOINT must be set}
```

**REQ-07-013: Variable Expansion**
- Expand variables before pipeline execution
- Expand in step commands, environment variables, arguments
- Preserve unexpanded variables if not found (log warning)
- Detect circular references (A→B→A) and error
- Support nested expansion up to 10 levels deep
- Expand at runtime (not parse time) for dynamic values

**REQ-07-014: Built-in Variables**
Provide these built-in variables automatically:
- `PDK_VERSION`: PDK version (e.g., "1.0.0")
- `PDK_WORKSPACE`: Workspace directory path
- `PDK_RUNNER`: Current runner (e.g., "ubuntu-latest")
- `PDK_JOB`: Current job name
- `PDK_STEP`: Current step name/index
- `HOME`: User home directory
- `USER`: Current user
- `PWD`: Current working directory
- `TIMESTAMP`: Current timestamp (ISO 8601)
- `TIMESTAMP_UNIX`: Unix timestamp

**REQ-07-015: CLI Variable Override**
- Support `--var KEY=VALUE` flag (repeatable)
- Support `--var-file path/to/vars.json` to load variables from file
- CLI variables override all other sources
- Example:
  ```bash
  pdk run --var BUILD_CONFIG=Debug --var VERSION=1.2.3
  pdk run --var-file ./build-vars.json
  ```

**REQ-07-016: Environment Variable Patterns**
Support special environment variable patterns:
- `PDK_VAR_*`: Any env var starting with `PDK_VAR_` is available as variable
  - `PDK_VAR_BUILD_CONFIG=Release` → `BUILD_CONFIG=Release`
- `PDK_SECRET_*`: Treated as secrets (see FR-07-003)
- Standard environment variables also available

#### Acceptance Criteria
- ✅ Variables resolved from all sources with correct precedence
- ✅ Interpolation syntax works in pipeline files
- ✅ Default values work: `${VAR:-default}`
- ✅ Required variable errors: `${VAR:?error}`
- ✅ Circular reference detection works
- ✅ Built-in variables available automatically
- ✅ CLI `--var` flag overrides other sources
- ✅ Environment variable patterns recognized
- ✅ Unit tests cover resolution logic
- ✅ Integration tests verify expansion in pipelines

---

### FR-07-003: Secret Management
**Priority:** High  
**Complexity:** Medium  
**Dependencies:** FR-07-002 (Variable Resolution System)

#### Description
Implement secure secret management with output masking, encryption at rest, and clear separation between secrets and regular variables.

#### Requirements

**REQ-07-020: Secret Storage**
- Secrets stored separately from regular variables
- Encryption at rest using machine-specific key
- Use Data Protection API (DPAPI on Windows, Keychain on macOS, keyring on Linux)
- Secrets stored in: `~/.pdk/secrets.json` (encrypted)
- Never store secrets in plain text configuration files
- Support secret rotation

**REQ-07-021: Secret Definition**
Support defining secrets through:
1. **CLI**: `--secret NAME=VALUE` (value masked in logs)
2. **Environment variables**: `PDK_SECRET_*` pattern
3. **Interactive prompt**: `pdk secret set NAME` (masked input)
4. **Encrypted config**: Reference in config, actual value encrypted separately

**REQ-07-022: Secret Access**
- Secrets accessible like variables: `${SECRET_NAME}`
- ISecretManager interface for programmatic access
- Secrets never returned in plain text logs
- GetSecret() method returns decrypted value (in-memory only)
- Secrets only decrypted when needed (lazy decryption)

**REQ-07-023: Secret Masking**
- Automatically mask secret values in all output:
  - Console output
  - Log files
  - Error messages
  - Progress reports
  - Execution summaries
- Replace with `***` (minimum 3 stars, max 10)
- Mask partial matches (if secret is substring of output)
- Case-insensitive masking
- Don't mask if value is less than 3 characters (likely not a real secret)

**REQ-07-024: Secret Commands**
Provide CLI commands for secret management:
```bash
# Set a secret (interactive, masked input)
pdk secret set API_KEY

# Set a secret from stdin
echo "secret-value" | pdk secret set API_KEY --stdin

# List secret names (not values)
pdk secret list

# Delete a secret
pdk secret delete API_KEY

# Rotate/update a secret
pdk secret set API_KEY --force
```

**REQ-07-025: Secret Security Best Practices**
- Never log secret values (even at DEBUG level)
- Never include secrets in error messages
- Secrets not visible in `pdk version --full` output
- Secrets cleared from memory after use (where possible)
- Warn if secret passed via CLI (visible in process list)
- Recommend environment variables or interactive input
- Document security considerations

**REQ-07-026: Secret Detection**
Automatically detect potential secrets:
- Values containing "password", "token", "key", "secret" in name
- Treat as secrets even if not explicitly marked
- Warn user if potential secret defined as regular variable
- Suggest using `PDK_SECRET_*` pattern or `--secret` flag

#### Acceptance Criteria
- ✅ Secrets encrypted at rest
- ✅ Secrets never appear in plain text in logs
- ✅ Secret masking works in all output
- ✅ CLI secret commands work correctly
- ✅ Environment variable pattern `PDK_SECRET_*` recognized
- ✅ Secret values accessible in pipelines via interpolation
- ✅ Warnings for potential secrets defined as variables
- ✅ Cross-platform encryption works (Windows/macOS/Linux)
- ✅ Unit tests cover secret encryption and masking
- ✅ Integration tests verify end-to-end secret handling

---

## Non-Functional Requirements

### NFR-07-001: Performance
- Configuration loading: < 100ms
- Variable resolution: < 1ms per variable
- Secret decryption: < 10ms per secret
- No noticeable impact on pipeline execution time
- Cache configuration and decrypted secrets in memory

### NFR-07-002: Security
- Secrets encrypted using industry-standard algorithms
- Encryption keys protected by OS-level key stores
- No secrets in process arguments (visible in ps/Task Manager)
- Secrets cleared from memory when no longer needed
- Audit log for secret access (optional, disabled by default)

### NFR-07-003: Usability
- Clear error messages for configuration problems
- Helpful suggestions when variables missing
- Documentation with examples for all features
- Migration guide for users coming from other CI systems
- Interactive secret input doesn't echo to terminal

### NFR-07-004: Compatibility
- Configuration format compatible with .gitignore (secrets excluded)
- Works across Windows, macOS, and Linux
- Respects XDG Base Directory specification on Linux
- Compatible with container environments (Docker)

### NFR-07-005: Testability
- All configuration logic unit testable
- Mock file system for testing
- Mock encryption for deterministic tests
- Test coverage target: 80%+

---

## Technical Specifications

### TS-07-001: Configuration Architecture

**Configuration Layers:**
```
┌─────────────────────────────┐
│    CLI Arguments            │ (Highest Precedence)
├─────────────────────────────┤
│    Environment Variables    │
├─────────────────────────────┤
│    User Config File         │
├─────────────────────────────┤
│    Default Configuration    │ (Lowest Precedence)
└─────────────────────────────┘
```

**Interfaces:**
```csharp
public interface IConfiguration
{
    string? GetString(string key, string? defaultValue = null);
    int GetInt(string key, int defaultValue = 0);
    bool GetBool(string key, bool defaultValue = false);
    T? GetSection<T>(string key) where T : class;
    bool TryGetValue(string key, out object? value);
    IEnumerable<string> GetKeys(string? section = null);
}

public interface IConfigurationLoader
{
    Task<PdkConfig> LoadAsync(string? configPath = null);
    Task<bool> ValidateAsync(string configPath);
}

public interface IConfigurationMerger
{
    PdkConfig Merge(params PdkConfig[] configs);
}
```

### TS-07-002: Variable Resolution Architecture

**Interfaces:**
```csharp
public interface IVariableResolver
{
    string? Resolve(string variableName);
    string ExpandVariables(string input);
    Dictionary<string, string> GetAllVariables();
    void SetVariable(string name, string value, VariableSource source);
}

public enum VariableSource
{
    BuiltIn,      // PDK_VERSION, etc.
    Configuration, // From config file
    Environment,   // From env vars
    CliArgument    // From --var
}

public interface IVariableExpander
{
    string Expand(string input, IVariableResolver resolver);
    bool ContainsVariables(string input);
    IEnumerable<string> ExtractVariableNames(string input);
}
```

**Expansion algorithm:**
1. Find all `${...}` patterns in input
2. For each pattern:
   - Extract variable name and default value
   - Resolve variable using IVariableResolver
   - If found, replace pattern with value
   - If not found and default exists, use default
   - If not found and required (:?), throw error
   - If not found otherwise, leave pattern unchanged (log warning)
3. Repeat up to 10 times for nested expansion
4. Return expanded string

### TS-07-003: Secret Management Architecture

**Interfaces:**
```csharp
public interface ISecretManager
{
    Task<string?> GetSecretAsync(string name);
    Task SetSecretAsync(string name, string value);
    Task DeleteSecretAsync(string name);
    Task<IEnumerable<string>> ListSecretNamesAsync();
    Task<bool> SecretExistsAsync(string name);
}

public interface ISecretEncryption
{
    byte[] Encrypt(string plaintext);
    string Decrypt(byte[] ciphertext);
}

public interface ISecretMasker
{
    string MaskSecrets(string text, IEnumerable<string> secrets);
    bool ContainsSecret(string text, string secret);
}
```

**Encryption strategy:**
- Windows: Use DPAPI (ProtectedData.Protect)
- macOS: Use Keychain (via Security.framework)
- Linux: Use Secret Service API (via libsecret)
- Fallback: AES-256 with machine-specific key derivation

**Secret storage format:**
```json
{
  "version": "1.0",
  "secrets": {
    "API_KEY": {
      "encryptedValue": "base64-encoded-encrypted-bytes",
      "algorithm": "DPAPI",
      "createdAt": "2024-01-15T10:30:00Z",
      "updatedAt": "2024-01-20T14:45:00Z"
    }
  }
}
```

### TS-07-004: Configuration File Schema

JSON Schema for validation:
```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "type": "object",
  "required": ["version"],
  "properties": {
    "version": {
      "type": "string",
      "enum": ["1.0"]
    },
    "variables": {
      "type": "object",
      "patternProperties": {
        "^[A-Z_][A-Z0-9_]*$": {
          "type": "string"
        }
      }
    },
    "docker": {
      "type": "object",
      "properties": {
        "defaultRunner": {"type": "string"},
        "memoryLimit": {"type": "string", "pattern": "^[0-9]+(k|m|g)$"},
        "cpuLimit": {"type": "number", "minimum": 0.1}
      }
    }
  }
}
```

---

## Dependencies

### External Dependencies
- **System.Text.Json** (≥8.0.0): JSON parsing
- **Microsoft.Extensions.Configuration** (≥8.0.0): Configuration abstractions
- **System.Security.Cryptography** (built-in): Encryption
- **Platform-specific**:
  - Windows: No additional dependencies (DPAPI built-in)
  - macOS: Security.framework (via P/Invoke or library)
  - Linux: libsecret (optional, fallback to file-based encryption)

### Internal Dependencies
- **Sprint 6 Phase 1**: SecretMasker (move/reuse from logging)
- **Sprint 4**: Job runner (for variable expansion integration)

---

## Testing Strategy

### Unit Testing
- Configuration parsing and validation
- Configuration merging logic
- Variable resolution with precedence
- Variable expansion with interpolation
- Secret encryption and decryption
- Secret masking algorithm
- CLI argument parsing

### Integration Testing
- Load configuration from actual files
- Variable resolution from multiple sources
- Secret storage and retrieval across restarts
- End-to-end variable expansion in pipelines
- Cross-platform encryption (Windows/macOS/Linux)

### Security Testing
- Verify secrets never logged
- Verify secrets encrypted at rest
- Test secret masking with edge cases
- Verify secrets cleared from memory (if possible)

### Test Data
- Sample configuration files (valid and invalid)
- Sample pipelines using variables
- Various secret values (different lengths, special chars)
- Edge cases for variable interpolation

---

## Success Metrics

### Functional Metrics
- All configuration sources working (file, env, CLI)
- Variable precedence rules enforced correctly
- Secret masking effective (0 secret leaks in tests)
- Test coverage: 80%+ for all configuration code

### Quality Metrics
- Configuration errors provide clear, actionable messages
- Documentation includes practical examples
- Migration from other CI systems is straightforward
- Secrets remain secure across all supported platforms

---

## Constraints and Assumptions

### Constraints
- Must work offline (no cloud-based secret storage)
- Must not require admin/root privileges
- Encryption must use OS-provided mechanisms where available
- Configuration file must be git-committable (excluding secrets)

### Assumptions
- Users have basic understanding of environment variables
- Users will not commit secrets to version control
- OS-level key stores are available and functional
- JSON is acceptable format (vs YAML, TOML, etc.)

---

## Future Considerations

### Post-Sprint Enhancements
- YAML configuration format support
- Cloud-based secret providers (Azure Key Vault, AWS Secrets Manager)
- Secret sharing across team members
- Configuration profiles (dev, staging, prod)
- Variable templates and inheritance
- Config file includes/imports
- Validation of variable references in pipelines

### Technical Debt
- Consider more sophisticated secret detection heuristics
- Evaluate performance of masking with many secrets
- Assess memory security (zeroing buffers)
- Consider audit logging for secret access

---

## Appendix

### A. Configuration File Examples

**Basic configuration:**
```json
{
  "version": "1.0",
  "variables": {
    "BUILD_CONFIG": "Release",
    "NODE_VERSION": "18.x"
  },
  "docker": {
    "defaultRunner": "ubuntu-latest",
    "memoryLimit": "4g"
  }
}
```

**Advanced configuration:**
```json
{
  "version": "1.0",
  "variables": {
    "BUILD_CONFIG": "Release",
    "DOCKER_REGISTRY": "myregistry.azurecr.io",
    "VERSION": "1.0.0"
  },
  "docker": {
    "defaultRunner": "ubuntu-latest",
    "memoryLimit": "4g",
    "cpuLimit": "2.0",
    "network": "bridge"
  },
  "artifacts": {
    "basePath": ".pdk/artifacts",
    "retentionDays": 30,
    "compression": "gzip"
  },
  "logging": {
    "level": "Debug",
    "file": "./pdk-debug.log"
  },
  "features": {
    "checkUpdates": false
  }
}
```

### B. Variable Usage Examples

**In GitHub Actions workflow:**
```yaml
name: Build
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Build
        run: dotnet build --configuration ${BUILD_CONFIG:-Debug}
      - name: Push
        run: docker push ${DOCKER_REGISTRY}/myapp:${VERSION}
```

**CLI usage:**
```bash
# Use configuration file
pdk run --file workflow.yml

# Override with environment variable
BUILD_CONFIG=Debug pdk run --file workflow.yml

# Override with CLI argument
pdk run --file workflow.yml --var BUILD_CONFIG=Debug

# Multiple variables
pdk run --var VERSION=1.2.3 --var REGISTRY=localhost:5000

# Required variable
pdk run --file workflow.yml
# Error: DOCKER_REGISTRY is required (from ${DOCKER_REGISTRY:?})
```

### C. Secret Management Examples

**Setting secrets:**
```bash
# Interactive (recommended)
pdk secret set API_KEY
# Prompts: Enter value for API_KEY: [input masked]

# From stdin
echo "my-secret-value" | pdk secret set API_KEY --stdin

# From environment
export PDK_SECRET_API_KEY="my-secret-value"
pdk run --file workflow.yml

# Via CLI (not recommended - visible in process list)
pdk run --secret API_KEY=my-secret-value
```

**Using secrets in pipelines:**
```yaml
steps:
  - name: Deploy
    run: |
      curl -H "Authorization: Bearer ${API_KEY}" \
           https://api.example.com/deploy
```

Output will show:
```
[Deploy] + curl -H "Authorization: Bearer ***" https://api.example.com/deploy
```

### D. Glossary

- **Configuration**: Persistent settings stored in files
- **Variable**: Named value that can be referenced in pipelines
- **Secret**: Sensitive variable that is encrypted and masked
- **Interpolation**: Replacing variable references with values
- **Precedence**: Order in which variable sources override each other
- **Built-in Variable**: System-provided variable (PDK_VERSION, etc.)
- **Expansion**: Process of resolving variable references

---

**Document Status:** Ready for Implementation  
**Next Steps:** Review with stakeholders, begin Sprint 7 implementation  
**Change History:**
- 2024-11-30: v1.0 - Initial requirements document
