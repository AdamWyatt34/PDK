# Sprint 2: Azure DevOps Parser - Requirements Document
---

## 1. Overview

### 1.1 Purpose
This document defines the functional and technical requirements for implementing Azure DevOps Pipeline YAML parsing capabilities in the Pipeline Development Kit (PDK).

### 1.2 Scope
This sprint covers:
- Azure Pipelines YAML parsing
- Mapping Azure structures to PDK's common pipeline model
- Support for core Azure Pipeline tasks
- Comprehensive test coverage

### 1.3 Out of Scope
- Azure DevOps API integration
- Template resolution
- Variable group resolution
- Classic (non-YAML) pipeline support
- Pipeline execution (covered in future sprints)

---

## 2. Requirements

### 2.1 Functional Requirements

#### REQ-AZ-001: Parse Azure Pipeline YAML Files
**Priority:** P0 (Critical)  
**Description:** The system SHALL parse valid Azure Pipelines YAML files into structured objects.

**Acceptance Criteria:**
- AC1: System accepts `.yml` and `.yaml` file extensions
- AC2: System deserializes valid Azure Pipeline YAML into typed objects
- AC3: System reports syntax errors with line numbers
- AC4: System validates required fields and reports missing values

**Testability:** Unit tests with valid/invalid YAML samples

---

#### REQ-AZ-002: Support Pipeline Root Structure
**Priority:** P0 (Critical)  
**Description:** The system SHALL support the root-level Azure Pipeline structure.

**Required Elements:**
- `name` (optional) - Pipeline name
- `trigger` (optional) - Trigger configuration
- `pool` (optional) - Default pool configuration
- `variables` (optional) - Pipeline variables
- `stages` (optional) - Multi-stage pipeline definition
- `jobs` (optional) - Single-stage pipeline definition
- `steps` (optional) - Simple pipeline definition

**Acceptance Criteria:**
- AC1: System parses pipelines with `stages` → `jobs` → `steps` hierarchy
- AC2: System parses pipelines with `jobs` → `steps` hierarchy (no stages)
- AC3: System parses pipelines with direct `steps` (no stages or jobs)
- AC4: System inherits pipeline-level `pool` to jobs when not specified at job level

**Testability:** Integration tests with real-world pipeline samples

---

#### REQ-AZ-003: Support Trigger Configuration
**Priority:** P1 (High)  
**Description:** The system SHALL parse trigger configurations.

**Required Sub-elements:**
- `branches` - Include/exclude branch patterns
- `paths` - Include/exclude path patterns
- `tags` - Include/exclude tag patterns

**Acceptance Criteria:**
- AC1: System parses branch inclusion/exclusion rules
- AC2: System parses path-based triggers
- AC3: System parses tag-based triggers
- AC4: System handles wildcard patterns (`*`, `feature/*`)

**Testability:** Unit tests for trigger parsing

---

#### REQ-AZ-004: Support Pool Configuration
**Priority:** P0 (Critical)  
**Description:** The system SHALL parse pool configurations for agent selection.

**Required Configurations:**
- Microsoft-hosted pools: `vmImage` property
- Self-hosted pools: `name` property with optional `demands`

**Acceptance Criteria:**
- AC1: System parses `vmImage` for Microsoft-hosted agents
- AC2: System parses `name` for self-hosted pools
- AC3: System parses agent `demands` (capabilities required)
- AC4: System validates pool configuration completeness

**Testability:** Unit tests with various pool configurations

---

#### REQ-AZ-005: Support Variable Definitions
**Priority:** P1 (High)  
**Description:** The system SHALL parse pipeline variable definitions.

**Required Formats:**
```yaml
# Simple name/value pairs
variables:
  variableName: 'value'
  
# List format
variables:
  - name: 'var1'
    value: 'value1'
  - group: 'variableGroupName'  # Out of scope for resolution
```

**Acceptance Criteria:**
- AC1: System parses simple name/value variable syntax
- AC2: System parses list-based variable syntax
- AC3: System records variable group references (does not resolve)
- AC4: System preserves variable names and values

**Testability:** Unit tests for variable parsing

---

#### REQ-AZ-006: Support Stage Definitions
**Priority:** P1 (High)  
**Description:** The system SHALL parse stage definitions in multi-stage pipelines.

**Required Properties:**
- `stage` - Stage identifier (required)
- `displayName` - Human-readable name
- `dependsOn` - Stage dependencies
- `condition` - Execution condition
- `jobs` - Jobs within the stage

**Acceptance Criteria:**
- AC1: System parses stage identifier and display name
- AC2: System captures stage dependencies (`dependsOn`)
- AC3: System preserves condition expressions
- AC4: System parses all jobs within a stage

**Testability:** Integration tests with multi-stage pipelines

---

#### REQ-AZ-007: Support Job Definitions
**Priority:** P0 (Critical)  
**Description:** The system SHALL parse job definitions.

**Required Properties:**
- `job` - Job identifier (required)
- `displayName` - Human-readable name
- `pool` - Job-specific pool (overrides pipeline pool)
- `dependsOn` - Job dependencies
- `condition` - Execution condition
- `timeoutInMinutes` - Job timeout
- `steps` - Job steps

**Acceptance Criteria:**
- AC1: System parses job identifier and display name
- AC2: System handles job-level pool override
- AC3: System captures job dependencies
- AC4: System parses all steps within a job

**Testability:** Unit tests for job parsing

---

#### REQ-AZ-008: Support Step Definitions
**Priority:** P0 (Critical)  
**Description:** The system SHALL parse step definitions.

**Required Formats:**
- Task syntax: `task: TaskName@version`
- Script shortcuts: `bash`, `pwsh`, `script`
- Inline scripts vs. file references

**Acceptance Criteria:**
- AC1: System parses task syntax with version
- AC2: System parses task `inputs` as key-value pairs
- AC3: System parses script shortcuts (`bash`, `pwsh`, `script`)
- AC4: System distinguishes inline scripts from file paths
- AC5: System captures `displayName`, `condition`, `enabled` properties

**Testability:** Unit tests for each step format

---

#### REQ-AZ-009: Support Common Azure Tasks
**Priority:** P0 (Critical)  
**Description:** The system SHALL recognize and parse common Azure Pipeline tasks.

**Required Tasks:**
- `DotNetCoreCLI@2` - .NET Core CLI operations
- `PowerShell@2` - PowerShell script execution
- `Bash@3` - Bash script execution
- `Docker@2` - Docker operations
- `CmdLine@2` - Command line execution

**Acceptance Criteria:**
- AC1: System parses `DotNetCoreCLI@2` with inputs: `command`, `projects`, `arguments`
- AC2: System parses `PowerShell@2` with inputs: `targetType`, `script`, `filePath`
- AC3: System parses `Bash@3` with inputs: `targetType`, `script`, `filePath`
- AC4: System parses `Docker@2` with inputs: `command`, `Dockerfile`, `tags`
- AC5: System parses `CmdLine@2` with input: `script`

**Testability:** Integration tests for each task type

---

#### REQ-AZ-010: Map to Common Pipeline Model
**Priority:** P0 (Critical)  
**Description:** The system SHALL convert Azure Pipeline structures to PDK's common pipeline model.

**Mapping Requirements:**

| Azure Structure | Common Model | Mapping Rule |
|----------------|--------------|--------------|
| `stages` → `jobs` | `jobs` | Flatten: stage name becomes job prefix |
| `task: Name@version` | `step.type` | Extract task name |
| `pool.vmImage` | `job.runner` | Direct mapping |
| `$(variable)` | `${variable}` | Syntax conversion |
| `condition:` | `step.condition` | Preserve as string |

**Acceptance Criteria:**
- AC1: Multi-stage pipelines flatten to jobs with naming convention: `{stage}_{job}`
- AC2: Azure tasks map to common step types (e.g., `DotNetCoreCLI@2` → type: `dotnet`)
- AC3: Pool configurations map to runner specifications
- AC4: Variable syntax converts from `$(var)` to `${var}`
- AC5: Original Azure structure preserved in metadata for debugging

**Testability:** Integration tests comparing Azure input to common model output

---

#### REQ-AZ-011: Handle Edge Cases
**Priority:** P1 (High)  
**Description:** The system SHALL handle common edge cases gracefully.

**Edge Cases:**

1. **Missing Pool Definition**
    - Jobs without explicit pool SHALL inherit pipeline-level pool
    - If no pool defined anywhere, use default: `ubuntu-latest`

2. **Empty or Null Values**
    - Empty `displayName` SHALL default to job/stage identifier
    - Null `steps` array SHALL result in validation error

3. **Condition Expressions**
    - Complex conditions SHALL be preserved as strings
    - Invalid condition syntax SHALL be reported as warnings

4. **Duplicate Identifiers**
    - Duplicate stage/job names SHALL result in validation error

**Acceptance Criteria:**
- AC1: Pool inheritance works correctly for nested structures
- AC2: Defaults are applied consistently
- AC3: Validation errors provide clear, actionable messages
- AC4: System does not crash on unexpected input

**Testability:** Unit tests for each edge case

---

### 2.2 Non-Functional Requirements

#### REQ-AZ-NFR-001: Performance
**Description:** Parsing SHALL complete within acceptable time limits.

**Requirements:**
- Parse a 100-line pipeline file in < 100ms
- Parse a 1000-line pipeline file in < 500ms

**Testability:** Performance benchmarks

---

#### REQ-AZ-NFR-002: Error Reporting
**Description:** Errors SHALL be clear and actionable.

**Requirements:**
- Include file name and line number
- Suggest fixes for common mistakes
- Distinguish between syntax errors and validation errors

**Example:**
```
Error: Missing required field 'job' in job definition
File: azure-pipelines.yml
Line: 15
Suggestion: Add a unique identifier: job: MyJobName
```

**Testability:** Manual testing of error scenarios

---

#### REQ-AZ-NFR-003: Test Coverage
**Description:** Code SHALL have minimum 80% test coverage.

**Requirements:**
- Unit tests for all parser logic
- Integration tests with real-world examples
- Edge case coverage

**Testability:** Code coverage reports

---

#### REQ-AZ-NFR-004: Code Quality
**Description:** Code SHALL follow .NET best practices.

**Requirements:**
- XML documentation on all public APIs
- Nullable reference types enabled
- Async/await used consistently
- Modern C# syntax (records, file-scoped namespaces)

**Testability:** Code review checklist

---

## 3. Success Criteria

The sprint is considered successful when:

1. ✅ All P0 requirements implemented and tested
2. ✅ All tests passing with 80%+ coverage
3. ✅ Can parse standard Azure .NET build pipeline
4. ✅ Can parse multi-stage pipeline
5. ✅ `pdk validate --file azure-pipelines.yml` command works end-to-end
6. ✅ No known P0/P1 bugs

---

## 4. Dependencies

### 4.1 Internal Dependencies
- Sprint 0 complete (core models defined)
- `IPipelineParser` interface exists
- Common `Pipeline`, `Job`, `Step` models defined

### 4.2 External Dependencies
- YamlDotNet library for YAML parsing
- xUnit, FluentAssertions for testing

---

## 5. Assumptions and Constraints

### 5.1 Assumptions
- Azure Pipelines YAML schema is stable
- Users have valid YAML files
- Template files will be handled in a future sprint

### 5.2 Constraints
- No network calls (no API integration)
- No external file resolution (templates, variable groups)
- Parser is read-only (no YAML generation)

---

## 6. Testing Strategy

### 6.1 Unit Tests
- Test each model class deserializes correctly
- Test parser logic in isolation
- Test step mapping logic
- Mock file I/O operations

### 6.2 Integration Tests
- Parse real Azure Pipeline files from actual projects
- Test complete parsing flow: YAML → Azure models → Common model
- Verify output against expected structure

### 6.3 Test Data
Maintain a test suite of Azure Pipeline files:
- Simple pipeline (direct steps)
- Single-stage pipeline (jobs + steps)
- Multi-stage pipeline (stages + jobs + steps)
- Complex .NET build pipeline
- Pipeline with all common tasks
- Edge cases (missing values, inheritance)

---

## 7. Deliverables

1. **Code:**
    - `src/PDK.Providers/AzureDevOps/` - All parser code
    - Azure model classes
    - `AzureDevOpsParser` implementation
    - `AzureStepMapper` implementation

2. **Tests:**
    - `tests/PDK.Tests.Unit/Providers/AzureDevOps/` - Unit tests
    - `tests/PDK.Tests.Integration/` - Integration tests
    - Test coverage report

3. **Documentation:**
    - XML comments on all public APIs
    - Sample Azure Pipeline files in `samples/azure/`

4. **Integration:**
    - Parser registered in DI container
    - `pdk validate` command supports Azure Pipelines

---

## 8. Acceptance Testing

Before marking sprint complete, verify:

```bash
# Test 1: Validate a simple pipeline
pdk validate --file samples/azure/simple-pipeline.yml
# Expected: ✓ Valid pipeline with 1 job, 3 steps

# Test 2: Validate a multi-stage pipeline
pdk validate --file samples/azure/multistage-pipeline.yml
# Expected: ✓ Valid pipeline with 2 stages, 3 jobs, 8 steps

# Test 3: Validate an invalid pipeline
pdk validate --file samples/azure/invalid-pipeline.yml
# Expected: ✗ Error at line X: Missing required field

# Test 4: Run all tests
dotnet test --configuration Release
# Expected: All tests pass, coverage > 80%
```

---

## 9. Risks and Mitigation

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|------------|
| Azure YAML schema complexity | High | Medium | Start with common patterns, expand incrementally |
| Variable syntax differences | Medium | Medium | Create comprehensive test suite for variable formats |
| Stage-to-job flattening logic | Medium | High | Design mapping strategy early, validate with examples |
| Unknown Azure tasks | Medium | Low | Log warnings for unsupported tasks, document limitations |
