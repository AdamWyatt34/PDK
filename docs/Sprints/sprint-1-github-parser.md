# Sprint 1: GitHub Actions Parser - Requirements

## Sprint Overview

**Goal:** Parse GitHub Actions workflows into PDK's common pipeline model

**Duration Estimate:** 16-23 hours (flexible based on availability)

**Status:** 🚧 In Progress

---

## What We're Building

The GitHub Actions parser enables PDK to understand and interpret GitHub Actions workflow files (`.github/workflows/*.yml`). It must:

1. Read and deserialize GitHub Actions YAML files
2. Convert GitHub-specific structures into PDK's common pipeline model
3. Validate workflows and provide helpful error messages
4. Handle the most common GitHub Actions patterns

This is the first parser implementation and will serve as a reference for future parsers (Azure DevOps, GitLab CI).

---

## Functional Requirements

### FR1: Parse GitHub Actions Workflow Files

**Must support:**
- Workflow metadata: `name`, `on` (triggers)
- Jobs: `jobs` section with job definitions
- Steps: both `uses` (actions) and `run` (scripts)
- Environment variables: workflow, job, and step level
- Runner specification: `runs-on` values
- Job dependencies: `needs` field

**Must handle:**
- Multiple jobs in a single workflow
- Jobs with multiple steps
- Steps with and without names (generate defaults for unnamed steps)
- Both simple and complex trigger definitions (`on`)

### FR2: Support Common GitHub Actions

**Must recognize and map these actions:**
- `actions/checkout@v*` → Checkout step
- `actions/setup-dotnet@v*` → .NET setup step
- `actions/setup-node@v*` → Node.js setup step
- `run` commands → Script steps (bash/PowerShell/sh)

**For each action, extract:**
- Action parameters from `with` section
- Environment variables from `env` section
- Working directory if specified
- Shell type for `run` steps

### FR3: Convert to Common Pipeline Model

**Must produce a valid `Pipeline` object containing:**
- Pipeline name (from workflow name or filename)
- List of `Job` objects
- Each job containing:
  - Job ID and name
  - Runner/environment specification
  - List of `Step` objects
  - Environment variables
  - Dependencies on other jobs
- Each step containing:
  - Step type (Checkout, Script, Dotnet, Npm, etc.)
  - Step name
  - Configuration/parameters
  - Environment variables

### FR4: Validation and Error Handling

**Must validate:**
- File exists and is readable
- YAML is well-formed
- Required fields are present:
  - `jobs` section exists
  - Each job has `runs-on`
  - Each job has at least one step
- Steps have either `uses` or `run` (not both, not neither)

**Must provide clear error messages for:**
- Invalid YAML syntax (with line numbers if possible)
- Missing required fields
- Unsupported action types (with suggestions)
- Circular job dependencies
- Invalid action reference format

### FR5: Parser Detection

**Must implement `CanParseAsync` to detect:**
- File is a GitHub Actions workflow
- Key indicators:
  - Contains `jobs:` section
  - Jobs contain `runs-on:` field
  - File path matches `.github/workflows/*.yml` pattern (optional check)

---

## Non-Functional Requirements

### NFR1: Code Quality
- All public APIs must have XML documentation
- Follow Clean Architecture principles
- Use dependency injection
- Async/await throughout
- Modern C# 12 features (file-scoped namespaces, records, primary constructors)

### NFR2: Testing
- Minimum 80% code coverage
- Unit tests for all parsing logic
- Integration tests with real workflow files
- Test error handling paths
- Test edge cases (missing values, defaults, etc.)

### NFR3: Performance
- Parse typical workflow (<100 steps) in under 100ms
- No unnecessary allocations
- Use async I/O for file operations

### NFR4: Maintainability
- Clear separation between GitHub models and common models
- Easy to extend with new action types
- Logging at appropriate levels
- Fail fast with descriptive errors

---

## Acceptance Criteria

### ✅ Sprint Complete When:

1. **Parsing Works**
   - Can parse a standard .NET build workflow
   - Can parse a Node.js build workflow
   - Can parse a workflow with multiple jobs
   - Can parse workflows with job dependencies

2. **Tests Pass**
   - All unit tests pass
   - All integration tests pass
   - Code coverage ≥ 80%
   - Tests cover success and failure scenarios

3. **CLI Integration**
   - `pdk validate --file .github/workflows/ci.yml` works
   - Shows validation errors with helpful messages
   - Returns appropriate exit codes

4. **Documentation**
   - README updated with GitHub Actions support
   - Sample workflows included in repository
   - Known limitations documented
   - XML documentation complete

---

## Deliverables

### Code Artifacts

**Required Files:**
```
src/PDK.Providers/GitHub/
  ├── Models/
  │   ├── GitHubWorkflow.cs      # Top-level workflow model
  │   ├── GitHubJob.cs            # Job model
  │   ├── GitHubStep.cs           # Step model
  │   └── [Additional models as needed]
  ├── GitHubActionsParser.cs      # Main parser implementation
  └── [Additional classes as needed]

tests/PDK.Tests.Unit/Providers/GitHub/
  ├── GitHubActionsParserTests.cs
  └── [Additional test files]

tests/PDK.Tests.Integration/
  ├── GitHubParserIntegrationTests.cs
  └── Fixtures/
      ├── dotnet-build.yml        # Sample .NET workflow
      ├── node-build.yml          # Sample Node.js workflow
      └── multi-job.yml           # Sample multi-job workflow
```

### Test Artifacts

**Unit Tests Must Cover:**
- Valid workflow parsing (happy path)
- Invalid YAML handling
- Missing required fields
- Default value generation (e.g., unnamed steps)
- Environment variable merging
- Job dependency parsing
- Different trigger formats
- Action reference parsing

**Integration Tests Must Cover:**
- Real .NET build workflow
- Real Node.js build workflow
- Multi-job workflow with dependencies
- Workflow with complex step configurations
- End-to-end: file → parsed Pipeline

---

## Constraints and Assumptions

### In Scope for Sprint 1
- Basic workflow structure parsing
- Common actions: checkout, setup-dotnet, setup-node, run scripts
- Simple environment variables (string values)
- Basic job dependencies (needs field)
- Simple trigger definitions

### Out of Scope for Sprint 1
- Matrix builds (will be a future sprint)
- Reusable workflows
- Composite actions
- Service containers
- Conditional expressions (if) - parse but don't evaluate
- Secrets (parse but don't resolve)
- Outputs (parse but don't handle)
- Artifacts (will be Sprint 8)
- Complex trigger definitions (schedule, workflow_dispatch with inputs)

### Assumptions
- YamlDotNet is already available and configured
- Common pipeline models (Pipeline, Job, Step) are complete
- IPipelineParser interface is defined and stable
- Running steps is Sprint 4 - this sprint only parses

---

## Reference Materials

### GitHub Actions Documentation
- [Workflow syntax](https://docs.github.com/en/actions/using-workflows/workflow-syntax-for-github-actions)
- [Common actions](https://github.com/actions)
- [Expressions](https://docs.github.com/en/actions/learn-github-actions/expressions)

### Example Workflows to Parse

**Simple .NET Build:**
```yaml
name: .NET Build
on: [push, pull_request]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0'
      - run: dotnet build
      - run: dotnet test
```

**Node.js with Multiple Jobs:**
```yaml
name: Node.js CI
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-node@v3
        with:
          node-version: '18'
      - run: npm ci
      - run: npm run build
  
  test:
    runs-on: ubuntu-latest
    needs: build
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-node@v3
        with:
          node-version: '18'
      - run: npm ci
      - run: npm test
```

---

## Success Metrics

**How we'll know this sprint succeeded:**

1. **Functional Success**
   - Parser correctly handles 3+ diverse real-world workflows
   - All common actions are recognized and mapped
   - Error messages are clear and actionable

2. **Technical Success**
   - Test coverage ≥ 80%
   - All tests pass consistently
   - Code review shows clean architecture
   - No code smells or anti-patterns

3. **User Success**
   - Can validate a workflow with `pdk validate`
   - Error messages help users fix problems
   - Workflow validation takes <100ms

---

## Out of Scope

**These are explicitly NOT part of Sprint 1:**
- Running/executing workflows (Sprint 4)
- Azure DevOps support (Sprint 2)
- Docker integration (Sprint 3)
- Artifact handling (Sprint 8)
- Matrix strategies (post-v1.0)
- Advanced GitHub Actions features (reusable workflows, composite actions)

---

## Definition of Done

Sprint 1 is complete when:

- [ ] All functional requirements are met
- [ ] All non-functional requirements are met
- [ ] All acceptance criteria are satisfied
- [ ] All deliverables are produced
- [ ] Code is committed and PR is ready
- [ ] Documentation is updated
- [ ] Can demo parsing a workflow end-to-end
- [ ] No known blocking issues
- [ ] Ready to proceed to Sprint 2

---

## Notes

- This is our first parser - it will serve as a pattern for others
- Focus on getting the architecture right, not on covering every edge case
- It's okay to document limitations - we can address them in future sprints
- Quality over speed - better to have a solid foundation