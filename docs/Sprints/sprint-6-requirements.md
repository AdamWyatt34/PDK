# PDK Sprint 6: CLI Polish & User Experience
## Requirements Document

**Document Version:** 1.0  
**Status:** Ready for Implementation  
**Sprint:** 6  
**Author:** PDK Development Team  
**Last Updated:** 2024-11-30  

---

## Executive Summary

Sprint 6 focuses on transforming PDK from a functional tool into a delightful developer experience. This sprint delivers polished CLI commands, real-time feedback, interactive workflows, and comprehensive error handling that makes PDK intuitive and pleasant to use.

### Goals
- Deliver professional-quality terminal UI with Spectre.Console
- Provide real-time visibility into pipeline execution
- Enable interactive pipeline exploration and execution
- Offer helpful, actionable error messages for common scenarios
- Build confidence through clear version and system information

### Success Criteria
- CLI feels intuitive and requires minimal documentation lookup
- Users can see progress in real-time during execution
- Error messages guide users to solutions
- Interactive mode enables easy pipeline exploration
- All features have 80%+ test coverage

---

## Feature Requirements

### FR-06-001: List Command
**Priority:** High  
**Complexity:** Low  
**Dependencies:** Sprint 1 (GitHub Parser), Sprint 2 (Azure Parser)

#### Description
Implement a `list` command that displays all jobs in a parsed pipeline with their metadata, providing users a clear overview of what will execute before running anything.

#### Requirements

**REQ-06-001: Command Invocation**
- Command syntax: `pdk list --file <path>`
- Support `-f` short alias for `--file`
- Default to auto-detection if `--file` not specified (`.github/workflows/*.yml`, `azure-pipelines.yml`)
- Exit code 0 on success, non-zero on error

**REQ-06-002: Job Information Display**
- Display jobs in a formatted table using Spectre.Console
- Show for each job:
  - Job ID/name
  - Runner/environment (e.g., "ubuntu-latest", "windows-latest")
  - Number of steps
  - Dependencies (if any)
  - Conditions (if any)
- Sort jobs by dependency order when possible

**REQ-06-003: Step Details**
- Option to show detailed steps: `pdk list --file <path> --details`
- Show for each step:
  - Step name/ID
  - Step type (checkout, script, action, task, etc.)
  - Key parameters (e.g., script content preview, action name)
- Truncate long content with ellipsis

**REQ-06-004: Output Format Options**
- Default: Rich table format with colors
- JSON output: `pdk list --file <path> --format json`
- Minimal output: `pdk list --file <path> --format minimal` (just job names)

#### Acceptance Criteria
- ✅ Can list jobs from GitHub Actions workflow
- ✅ Can list jobs from Azure DevOps pipeline
- ✅ Table output is properly formatted and aligned
- ✅ JSON output is valid and parseable
- ✅ Shows clear message when no jobs found
- ✅ Handles missing or invalid file gracefully
- ✅ Unit tests cover command logic
- ✅ Integration tests verify output formats

---

### FR-06-002: Enhanced Run Command Output
**Priority:** High  
**Complexity:** Medium  
**Dependencies:** Sprint 4 (Basic Job Runner)

#### Description
Transform the run command output from basic logging to a rich, real-time experience that shows execution progress, step status, and timing information as the pipeline runs.

#### Requirements

**REQ-06-010: Real-Time Progress Display**
- Show overall progress indicator: "Running job 2 of 5"
- Display current step being executed with spinner/progress animation
- Update in-place (don't scroll excessively)
- Show elapsed time for current step
- Use Spectre.Console Progress or Status components

**REQ-06-011: Step Status Visualization**
- Clear visual indicators for step states:
  - âš« Pending (not yet started)
  - ⚙ī¸ Running (currently executing)
  - âœ… Success (completed successfully)
  - ❌ Failed (error occurred)
  - ⏭ī¸ Skipped (condition not met)
- Color coding:
  - Gray for pending
  - Blue/cyan for running
  - Green for success
  - Red for failed
  - Yellow for skipped

**REQ-06-012: Step Output Streaming**
- Stream step output in real-time as it executes
- Prefix output lines with step name for clarity
- Preserve ANSI color codes from container output
- Option to suppress output: `--quiet`
- Option for verbose output: `--verbose` (includes Docker commands, etc.)

**REQ-06-013: Execution Summary**
- After pipeline completion, display summary table:
  - Total jobs run
  - Total steps executed
  - Success count
  - Failure count
  - Skipped count
  - Total execution time
- Show step timing breakdown for slowest steps
- Provide quick navigation to failed steps in output

**REQ-06-014: Error Context**
- When a step fails:
  - Show the exact command that failed
  - Display the last 20 lines of output
  - Show exit code
  - Highlight error messages in output
  - Suggest next steps (check logs, run with --verbose, etc.)

#### Acceptance Criteria
- ✅ Progress updates in real-time during execution
- ✅ Output is readable and not cluttered
- ✅ Failed steps are immediately obvious
- ✅ Summary provides actionable information
- ✅ Works in both CI and local terminals
- ✅ Respects --quiet and --verbose flags
- ✅ Unit tests verify output formatting logic
- ✅ Integration tests verify real-time behavior

---

### FR-06-003: Interactive Mode
**Priority:** Medium  
**Complexity:** Medium  
**Dependencies:** FR-06-001 (List Command), Sprint 4 (Basic Job Runner)

#### Description
Provide an interactive mode where users can explore pipeline structure and selectively execute jobs through a guided menu interface, making pipeline testing more intuitive.

#### Requirements

**REQ-06-020: Interactive Mode Activation**
- Command: `pdk run --interactive` or `pdk run -i`
- Also support: `pdk interactive <file>`
- Auto-detect pipeline file or prompt for selection
- Clear visual distinction that interactive mode is active

**REQ-06-021: Main Menu**
- Display options using Spectre.Console SelectionPrompt:
  1. View all jobs (show list)
  2. Run a specific job
  3. Run all jobs
  4. Show job details
  5. Exit
- Use arrow keys for navigation
- Highlight current selection

**REQ-06-022: Job Selection**
- When "Run a specific job" selected:
  - Show list of all jobs with descriptions
  - Display runner and step count for each
  - Allow user to select one or multiple jobs
  - Confirm before execution: "Run job 'build' on ubuntu-latest?"
  - Option to run with specific options (--verbose, --no-cache, etc.)

**REQ-06-023: Job Details View**
- When "Show job details" selected:
  - Prompt user to select a job
  - Display full job information:
    - All steps with names and types
    - Environment variables
    - Dependencies
    - Conditions
  - Options to:
    - Run this job
    - Back to main menu

**REQ-06-024: Execution in Interactive Mode**
- After selecting job(s) to run:
  - Show progress using same rich output as normal run
  - Return to menu after completion
  - Display quick summary (success/failure)
  - Option to view detailed logs
  - Option to run again with different settings

**REQ-06-025: User Experience**
- Keyboard shortcuts displayed in UI (e.g., "Press 'q' to quit")
- Breadcrumb navigation (show where you are in menu hierarchy)
- Color-coded status indicators
- Responsive to terminal resize
- Graceful handling of Ctrl+C (exit cleanly)

#### Acceptance Criteria
- ✅ Interactive mode launches without errors
- ✅ Menu navigation is smooth and intuitive
- ✅ Job selection and execution work correctly
- ✅ Can return to menu after job completion
- ✅ Keyboard shortcuts work as documented
- ✅ UI remains usable on different terminal sizes
- ✅ Unit tests cover menu logic
- ✅ Integration tests verify interactive flows

---

### FR-06-004: Error Handling & User Messages
**Priority:** High  
**Complexity:** Medium  
**Dependencies:** All previous sprints

#### Description
Replace generic errors with friendly, actionable error messages that guide users to solutions. Detect common failure scenarios and provide specific guidance for resolution.

#### Requirements

**REQ-06-030: Docker Availability Check**
- Before any Docker operation:
  - Check if Docker daemon is running
  - Verify Docker is accessible (permissions)
  - Check Docker version compatibility (minimum version required)
- If Docker unavailable:
  - Clear error message: "Docker is not running or not accessible"
  - Suggestions:
    - "Start Docker Desktop" (if on Windows/Mac)
    - "Check Docker service status: sudo systemctl status docker" (Linux)
    - "Try running with --host mode (executes on local machine without containers)"
  - Link to troubleshooting docs

**REQ-06-031: File and Path Errors**
- Pipeline file not found:
  - Message: "Could not find pipeline file: <path>"
  - Suggestions:
    - List valid pipeline files found in current directory
    - Show expected locations: ".github/workflows/*.yml", "azure-pipelines.yml"
    - Offer: "Run 'pdk list' to auto-detect pipeline files"
- Invalid YAML syntax:
  - Message: "Failed to parse YAML in <file>"
  - Show line number and column if available
  - Display problematic section (5 lines context)
  - Common fixes:
    - "Check for incorrect indentation"
    - "Verify quotes are balanced"
    - "Ensure list items start with '-'"

**REQ-06-032: Pipeline Structure Errors**
- Unsupported step type:
  - Message: "Step '<name>' uses unsupported action/task: <action>"
  - Show what IS supported (list available executors)
  - Link to request feature or contribute
- Missing required fields:
  - Message: "Job '<job>' is missing required field: <field>"
  - Explain what the field does
  - Provide example of correct syntax
- Circular dependencies:
  - Message: "Detected circular dependency in jobs"
  - Show the dependency chain
  - Suggest how to break the cycle

**REQ-06-033: Execution Errors**
- Container creation failure:
  - Message: "Failed to create container for job '<job>'"
  - Show Docker error (if available)
  - Suggestions:
    - "Check if image exists: docker pull <image>"
    - "Verify Docker has sufficient resources"
    - "Try with different runner: --runner ubuntu-20.04"
- Step execution failure:
  - Message: "Step '<step>' failed with exit code <code>"
  - Show full command that was executed
  - Display last 30 lines of output
  - Suggestions based on exit code:
    - Exit 127: "Command not found - ensure tool is installed in container"
    - Exit 1: "Check logs above for specific error"
    - Exit 137: "Container was killed - may have run out of memory"
- Timeout errors:
  - Message: "Step '<step>' exceeded timeout of <duration>"
  - Show how long it ran
  - Suggestions:
    - "Increase timeout with --timeout flag"
    - "Check if step is hanging (infinite loop?)"

**REQ-06-034: Permission and Access Errors**
- File permission errors:
  - Message: "Permission denied accessing <path>"
  - Suggestions:
    - "Check file permissions: ls -la <path>"
    - "Ensure PDK has write access to workspace"
- Network errors:
  - Message: "Network error: <description>"
  - Suggestions:
    - "Check internet connection"
    - "Verify proxy settings if behind corporate firewall"
    - "Check if Docker has network access"

**REQ-06-035: Error Context and Verbosity**
- All errors include:
  - Clear description of what went wrong
  - Context (what was being attempted)
  - Specific suggestions for fixing
  - Link to relevant documentation
  - Error code for lookup (e.g., "PDK-E001")
- Standard flag: `--verbose` shows:
  - Full stack traces
  - Docker command details
  - Environment variables (masked secrets)
  - Timing information
- Default (non-verbose) shows:
  - Clean error message
  - Essential context
  - Action items

**REQ-06-036: Error Recovery Suggestions**
- After error, offer:
  - "Run with --verbose for detailed diagnostics"
  - "Run 'pdk validate' to check pipeline syntax"
  - "Check logs: <path-to-log-file>"
- If error is known issue:
  - Link to GitHub issue or documentation
  - Show workaround if available

#### Acceptance Criteria
- ✅ Docker unavailable error is clear and helpful
- ✅ YAML syntax errors show line numbers and context
- ✅ Unsupported features provide alternatives
- ✅ Execution failures show actionable next steps
- ✅ Permission errors guide to resolution
- ✅ All error codes documented
- ✅ Verbose mode provides debug information
- ✅ Unit tests cover error detection
- ✅ Integration tests verify error messages

---

### FR-06-005: Version Command
**Priority:** Low  
**Complexity:** Low  
**Dependencies:** None

#### Description
Provide comprehensive version and system information to help with troubleshooting and support, showing versions of PDK itself and its dependencies.

#### Requirements

**REQ-06-040: Version Command**
- Command: `pdk version` or `pdk --version`
- Display:
  - PDK version (e.g., "1.0.0")
  - .NET runtime version
  - OS and architecture
  - Build date/commit hash

**REQ-06-041: System Information**
- Command: `pdk version --full` or `pdk version -v`
- Additional information:
  - Docker version (if available)
  - Docker daemon status (running/stopped)
  - Available providers (GitHub Actions, Azure DevOps, GitLab CI)
  - Installed step executors (dotnet, npm, docker, etc.)
  - Configuration file location (if found)

**REQ-06-042: Update Check (Optional)**
- Check for newer version of PDK on NuGet
- Display message if update available:
  - "A new version of PDK is available: 1.1.0 (you have 1.0.0)"
  - "Update with: dotnet tool update -g pdk"
- Only check if:
  - Not running in CI (check for CI environment variables)
  - Last check was > 24 hours ago
  - Can be disabled with config: `check_updates: false`
- Fail gracefully if network unavailable

**REQ-06-043: Output Format**
- Default: Human-readable format with labels and values
- JSON format: `pdk version --format json`
  - Structured output for scripting/automation
  - All version information as JSON object

#### Acceptance Criteria
- ✅ Shows accurate PDK version
- ✅ Shows accurate .NET runtime version
- ✅ Full version info includes Docker details
- ✅ Update check works without errors
- ✅ Gracefully handles offline mode
- ✅ JSON output is valid
- ✅ Unit tests verify version formatting
- ✅ Integration tests verify system detection

---

## Non-Functional Requirements

### NFR-06-001: Performance
- Command response time:
  - `list` command: < 500ms for typical pipeline
  - `version` command: < 100ms
  - Interactive mode launch: < 1 second
- UI updates should feel instant (< 50ms perceived latency)
- Progress indicators should update at least 10 times per second

### NFR-06-002: Accessibility
- Terminal output should work with screen readers
- Color coding should not be the only indicator (use symbols too)
- Support NO_COLOR environment variable for accessibility
- Respect terminal capabilities (fallback for limited terminals)

### NFR-06-003: Internationalization (Future)
- Error messages should be localizable (use resource files)
- Date/time formats should respect locale
- File paths should handle Unicode correctly

### NFR-06-004: Logging
- All user-facing messages should be logged
- Log levels:
  - ERROR: Problems that prevent execution
  - WARN: Issues that don't prevent execution
  - INFO: Normal operational messages
  - DEBUG: Detailed information for troubleshooting
- Log file location: `~/.pdk/logs/pdk.log` (rotating, max 10MB)
- Structured logging (JSON format) for machine parsing

### NFR-06-005: Testability
- All UI components should be testable without rendering
- Mock console output for unit tests
- Integration tests should capture actual terminal output
- Test coverage target: 80%+ for all new code

---

## Technical Specifications

### TS-06-001: Spectre.Console Integration
- Use Spectre.Console 0.47+ for all terminal UI
- Components to use:
  - `Table` for list command output
  - `Progress` or `Status` for run command
  - `SelectionPrompt` for interactive menus
  - `Panel` for information display
  - `Markup` for colored text
- Abstraction layer for testing (IAnsiConsole interface)

### TS-06-002: Error Code System
- Format: `PDK-{severity}-{component}-{number}`
  - Severity: E (error), W (warning), I (info)
  - Component: CLI, PARSER, RUNNER, DOCKER, etc.
  - Number: Unique identifier (001-999)
- Examples:
  - `PDK-E-DOCKER-001`: Docker daemon not running
  - `PDK-E-PARSER-010`: Invalid YAML syntax
  - `PDK-E-RUNNER-005`: Step execution failed
- Maintain error code catalog in documentation

### TS-06-003: Logging Architecture
- Use Microsoft.Extensions.Logging abstractions
- Configure providers:
  - Console provider for user-facing output
  - File provider for persistent logs
  - Structured logging provider for JSON
- Correlation IDs for request tracing
- Redact sensitive information (secrets, tokens) in logs

### TS-06-004: Interactive Mode State Machine
- States:
  - MainMenu
  - JobSelection
  - JobDetails
  - JobExecution
  - ExecutionComplete
- Transitions:
  - User input (arrow keys, enter, escape)
  - System events (execution complete, error)
- State should be testable without actual console I/O

### TS-06-005: Progress Reporting
- Use observer pattern for progress updates
- IProgressReporter interface:
  - `ReportProgress(double percentage, string message)`
  - `ReportStepStart(string stepName)`
  - `ReportStepComplete(string stepName, bool success)`
  - `ReportOutput(string line)`
- Console implementation uses Spectre.Console
- Test implementation captures events for verification

---

## Dependencies

### External Dependencies
- **Spectre.Console** (≄0.47.0): Terminal UI framework
- **Microsoft.Extensions.Logging** (≄8.0.0): Logging abstractions
- **System.CommandLine** (already in use): CLI framework

### Internal Dependencies
- **Sprint 1**: GitHub Actions parser (for list command)
- **Sprint 2**: Azure DevOps parser (for list command)
- **Sprint 4**: Basic job runner (for run command enhancements)

### Optional Dependencies
- **NuGet.Protocol** (for update check feature)

---

## Testing Strategy

### Unit Testing
- Command handler logic (without I/O)
- Error message formatting
- Progress calculation
- Interactive mode state transitions
- Version information gathering

### Integration Testing
- Full command execution with mocked console
- Error scenarios with various failure modes
- Interactive mode user flows (simulated input)
- Progress reporting during actual execution

### Manual Testing
- Visual verification of output formatting
- Terminal compatibility (various terminal emulators)
- Color scheme verification
- Interactive mode usability
- Error message clarity

### Test Data
- Sample pipelines from Sprints 1 and 2
- Invalid YAML files for error testing
- Large pipelines for performance testing
- Pipelines with various error conditions

---

## Success Metrics

### User Experience Metrics
- Time to first successful pipeline run (target: < 5 minutes)
- Error resolution rate (% of errors user can fix without external help)
- Command discoverability (can user find command without docs?)

### Technical Metrics
- Test coverage: 80%+ for all features
- Command response time: < 500ms for list, < 100ms for version
- Zero regressions in existing functionality
- All acceptance criteria met

### Quality Metrics
- Error messages are actionable (verified through user feedback)
- Interactive mode has < 5% navigation errors
- Progress reporting has no visual glitches
- Logging provides sufficient debug information

---

## Constraints and Assumptions

### Constraints
- Must work in both interactive and non-interactive terminals
- Must support Windows, macOS, and Linux
- Terminal width ≄80 characters (graceful degradation below)
- Cannot use platform-specific terminal features

### Assumptions
- Users have basic command-line familiarity
- Terminal supports ANSI color codes (or graceful fallback)
- .NET 8 runtime is installed
- Users can access internet for update checks (optional)

---

## Future Considerations

### Post-Sprint Enhancements
- Localization of error messages
- Custom error message templates
- Plugin system for custom error handlers
- Telemetry (opt-in) for common error patterns
- AI-assisted error resolution suggestions
- Integration with external error tracking systems

### Technical Debt
- Consider abstracting console I/O further for testing
- Evaluate performance of Spectre.Console with very large outputs
- Plan for terminal multiplexer compatibility (tmux, screen)
- Consider alternative UI for constrained environments

---

## Appendix

### A. Command Reference Quick Guide

```bash
# List all jobs in a pipeline
pdk list --file .github/workflows/ci.yml

# List with detailed steps
pdk list --file azure-pipelines.yml --details

# Run pipeline with enhanced output
pdk run --file .github/workflows/ci.yml

# Run in interactive mode
pdk run --interactive

# Show version information
pdk version

# Show full system information
pdk version --full

# Run with verbose error output
pdk run --file ci.yml --verbose
```

### B. Error Code Reference

| Error Code | Description | Common Resolution |
|------------|-------------|-------------------|
| PDK-E-DOCKER-001 | Docker daemon not running | Start Docker Desktop |
| PDK-E-PARSER-001 | YAML syntax error | Check file for indentation issues |
| PDK-E-PARSER-002 | Unsupported step type | Check supported actions list |
| PDK-E-RUNNER-001 | Step execution failed | Check step logs with --verbose |
| PDK-E-RUNNER-002 | Container creation failed | Verify Docker image exists |
| PDK-E-RUNNER-003 | Step timeout | Increase timeout with --timeout |

*(This will be expanded as features are implemented)*

### C. Glossary

- **Pipeline**: Complete CI/CD workflow definition
- **Job**: Independent unit of work within pipeline
- **Step**: Individual command or action within job
- **Runner**: Environment where job executes (e.g., ubuntu-latest)
- **Executor**: Component that runs specific step type (e.g., DotnetStepExecutor)
- **Provider**: Source of pipeline definition (GitHub, Azure DevOps, GitLab)

---

**Document Status:** Ready for Implementation  
**Next Steps:** Review with stakeholders, begin Sprint 6 implementation  
**Change History:**
- 2024-11-30: v1.0 - Initial requirements document
