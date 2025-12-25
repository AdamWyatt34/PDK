# Fail-Fast Approach

## Context

PDK needs to handle errors during pipeline execution. We needed to decide how to behave when errors occur: continue and collect all errors, or stop immediately?

## Decision

Adopt a fail-fast approach: stop execution on the first error and provide detailed, actionable feedback.

## Rationale

### 1. Fast Feedback

Developers want to know about problems quickly:

```
❌ Step 'Build' failed (exit code 1)

Error: CS0246: The type 'NonexistentClass' could not be found

Suggestion: Check if the class exists or if you're missing a using statement.
```

Continuing after a failed build would:
- Waste time running tests that can't pass
- Generate confusing secondary errors
- Delay the actual feedback developers need

### 2. Resource Efficiency

Stopping early saves resources:

| Scenario | Fail-Fast | Continue |
|----------|-----------|----------|
| Build fails | 30 seconds | 5 minutes |
| Network error | Immediate | 10 retries |
| Missing dep | Quick error | Cascading failures |

### 3. Clearer Error Messages

First error is usually the root cause:

```
# Fail-fast (clear)
❌ Error: Package 'Newtonsoft.Json' not found

# Continue-all (confusing)
❌ Package 'Newtonsoft.Json' not found
❌ Cannot resolve 'JsonConvert'
❌ Build failed with 47 errors
❌ Tests could not be run
❌ Artifact upload failed
```

### 4. CI/CD Behavior Match

CI systems typically fail-fast:
- GitHub Actions stops on first failure
- Azure DevOps fails the stage
- Most users expect this behavior

### 5. Predictable Execution

With fail-fast, execution is deterministic:
- Same input = same stopping point
- Easy to reproduce issues
- Clear cause-and-effect

## Implementation

### Step-Level Fail-Fast

```csharp
foreach (var step in job.Steps)
{
    var result = await ExecuteStepAsync(step);
    results.Add(result);

    if (!result.Success && !step.ContinueOnError)
    {
        // Stop immediately
        break;
    }
}
```

### ContinueOnError Override

For steps that can fail without stopping:

```yaml
steps:
  - name: Optional cleanup
    run: rm -rf temp/
    continue-on-error: true  # Won't stop pipeline

  - name: Build
    run: dotnet build  # Failure stops pipeline
```

### Job Dependencies

If a job fails, dependent jobs are skipped:

```yaml
jobs:
  build:
    steps: [...]  # If this fails...

  test:
    needs: build  # ...this is skipped
```

## Trade-offs

### Can't See All Errors at Once

Sometimes developers want to see all issues:
```
I want to fix all linting errors in one pass
```

**Mitigation:**
- Dry-run mode validates everything first
- Linting tools show all issues internally
- Individual step output is preserved

### Not Suitable for All Workflows

Some workflows benefit from continue-on-error:
```yaml
# Run all tests, report results
steps:
  - run: npm test --project A
    continue-on-error: true
  - run: npm test --project B
    continue-on-error: true
  - run: npm test --project C
    continue-on-error: true
  - run: aggregate-results
```

**Mitigation:**
- `continue-on-error` per step
- Future: `--continue-on-error` flag for all steps

### Partial Execution

Pipeline stops mid-way:
```
✓ Checkout
✓ Install
✗ Build
○ Test (skipped)
○ Deploy (skipped)
```

**Mitigation:**
- Clear status display
- Watch mode for quick re-runs
- Step filtering for targeted execution

## Alternatives Considered

### 1. Continue-on-Error by Default

Run all steps regardless of failures.

**Pros:**
- See all errors at once
- Better for test aggregation

**Cons:**
- Slower feedback
- Wasted resources
- Confusing cascading errors
- Doesn't match CI behavior

**Verdict**: Wrong for most use cases.

### 2. Configurable Behavior

Global flag to switch modes.

**Pros:**
- User choice
- Flexibility

**Cons:**
- Complex behavior
- Per-step `continue-on-error` sufficient
- Most users want fail-fast

**Verdict**: Considered for future.

### 3. Error Aggregation Then Fail

Collect all errors, show summary, then fail.

**Pros:**
- Complete error picture
- Still fails pipeline

**Cons:**
- Complex implementation
- Delayed feedback
- Resource waste

**Verdict**: Not worth complexity.

## Consequences

### Positive

1. Fast feedback loop
2. Clear error identification
3. Resource efficiency
4. Matches CI behavior
5. Predictable execution

### Negative

1. Can't see all errors at once
2. May need multiple runs to find all issues
3. Some workflows need explicit `continue-on-error`

### Implementation Impact

1. Step executor checks result after each step
2. `ContinueOnError` property on Step model
3. Skipped steps tracked in results
4. Exit code reflects first failure

## Status

**Accepted** - Fail-fast is the default behavior.

## References

- [Runner Architecture](../architecture/runners.md)
- [Execution Flow](../architecture/data-flow.md)
- [Watch Mode](../../configuration/watch-mode.md) - For quick iteration
