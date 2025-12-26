# Pull Request Process

This guide explains how to create, submit, and get your pull requests merged into PDK.

## Before You Start

1. **Check existing issues** - Is there already an issue or PR for this?
2. **Discuss significant changes** - Use [GitHub Discussions](https://github.com/AdamWyatt34/pdk/discussions) for new features
3. **Read the code standards** - Review [Code Standards](code-standards.md)

## Branching Strategy

### Create Your Branch

Start from the latest `main`:

```bash
git checkout main
git pull upstream main
git checkout -b <type>/<description>
```

### Branch Naming

| Type | Pattern | Example |
|------|---------|---------|
| Feature | `feature/<description>` | `feature/gitlab-parser` |
| Bug fix | `fix/<description>` | `fix/yaml-parsing-error` |
| Bug fix (with issue) | `fix/issue-<number>` | `fix/issue-123` |
| Documentation | `docs/<description>` | `docs/runner-architecture` |
| Refactoring | `refactor/<description>` | `refactor/executor-factory` |
| Tests | `test/<description>` | `test/parser-edge-cases` |

## Making Changes

### Commit Guidelines

Write clear commit messages following this format:

```
<type>: <subject>

<body>

<footer>
```

**Subject line:**
- Use imperative mood ("Add feature" not "Added feature")
- Keep under 50 characters
- No period at end

**Body:**
- Explain what and why, not how
- Wrap at 72 characters
- Include bullet points for multiple changes

**Footer:**
- Reference issues: `Closes #123` or `Fixes #123`
- Note breaking changes: `BREAKING CHANGE: description`

### Commit Types

| Type | Description |
|------|-------------|
| `feat` | New feature |
| `fix` | Bug fix |
| `docs` | Documentation only |
| `test` | Adding or updating tests |
| `refactor` | Code change that neither fixes a bug nor adds a feature |
| `perf` | Performance improvement |
| `chore` | Build process or auxiliary tool changes |

### Example Commits

```
feat: add support for GitLab CI pipelines

Implement GitLabCIParser to parse .gitlab-ci.yml files.
Maps GitLab CI jobs and stages to common pipeline model.

- Parse stages and jobs
- Map script and before_script sections
- Handle variable inheritance
- Add validation for required fields

Closes #123
```

```
fix: handle empty steps array in Azure pipelines

Azure DevOps allows jobs with no steps, but the parser
was throwing NullReferenceException. Now returns empty
step list instead.

Fixes #456
```

### Keep Commits Atomic

Each commit should be a single logical change:

- **Good**: Separate commits for feature, tests, and docs
- **Bad**: One giant commit with everything

### Rebase vs Merge

We prefer **rebasing** to keep history clean:

```bash
# Update your branch with latest main
git fetch upstream
git rebase upstream/main

# If there are conflicts, resolve them, then:
git add .
git rebase --continue
```

## Before Submitting

### Run Local Checks

```bash
# Build the project
dotnet build

# Run all tests
dotnet test

# Check for warnings
dotnet build /p:TreatWarningsAsErrors=true
```

### Self-Review Checklist

- [ ] Code compiles without warnings
- [ ] All tests pass
- [ ] New code has tests (80%+ coverage)
- [ ] Public APIs have XML documentation
- [ ] Code follows [Code Standards](code-standards.md)
- [ ] Commit messages are clear
- [ ] No unrelated changes included
- [ ] No debug code or console output left in

## Creating the Pull Request

### Push Your Branch

```bash
git push origin feature/my-feature
```

### Open PR on GitHub

1. Go to the repository on GitHub
2. Click "Compare & pull request"
3. Fill out the PR template

### PR Title

Follow the same format as commit messages:

```
feat: add GitLab CI pipeline parser
fix: handle empty steps in Azure pipelines
docs: add runner architecture documentation
```

### PR Description

Use this template:

```markdown
## Description

Brief description of what this PR does.

## Related Issues

Closes #123

## Changes

- Added GitLabCIParser class
- Implemented stage and job mapping
- Added validation for required fields

## Testing

- Added unit tests for parser
- Added integration test with sample .gitlab-ci.yml
- Manually tested with real GitLab repository

## Checklist

- [ ] Code compiles without warnings
- [ ] All tests pass
- [ ] New tests added for new functionality
- [ ] Documentation updated
- [ ] Follows code standards
```

### PR Size

**Keep PRs small and focused:**

| Size | Lines Changed | Review Time |
|------|---------------|-------------|
| Small | < 200 | < 30 min |
| Medium | 200-500 | 30-60 min |
| Large | 500+ | Consider splitting |

Large features should be split into multiple PRs when possible.

## Code Review Process

### What Happens After Submission

1. **CI Runs** - Automated tests and checks
2. **Review Assigned** - Maintainer reviews your code
3. **Feedback** - Comments and suggestions
4. **Updates** - You address feedback
5. **Approval** - Reviewer approves
6. **Merge** - PR is merged

### Responding to Feedback

- **Be responsive** - Try to address comments within a few days
- **Ask questions** - If feedback is unclear, ask for clarification
- **Push updates** - Add new commits to address feedback
- **Re-request review** - Click "Re-request review" when ready

### Addressing Comments

When addressing review comments:

```bash
# Make changes
git add .
git commit -m "Address review feedback"
git push origin feature/my-feature
```

For small fixes, consider amending the last commit:

```bash
git add .
git commit --amend --no-edit
git push --force-with-lease origin feature/my-feature
```

## CI Checks

Pull requests must pass these checks:

| Check | Description |
|-------|-------------|
| Build | Project compiles successfully |
| Unit Tests | All unit tests pass |
| Integration Tests | All integration tests pass |
| Code Coverage | Coverage meets threshold (80%) |
| Lint | No style violations |

### Fixing Failed Checks

1. Click "Details" on the failed check
2. Review the error message
3. Make necessary fixes locally
4. Push updates

## After Merge

### Delete Your Branch

```bash
# Delete local branch
git branch -d feature/my-feature

# Delete remote branch (usually done automatically)
git push origin --delete feature/my-feature
```

### Update Your Fork

```bash
git checkout main
git pull upstream main
git push origin main
```

## Special Cases

### Draft PRs

Use draft PRs for:
- Work in progress you want early feedback on
- Changes you're still testing
- Proposals for discussion

Convert to ready when complete: "Ready for review" button.

### Breaking Changes

For breaking changes:
1. Discuss in an issue first
2. Mark clearly in PR title: `feat!: remove deprecated API`
3. Document migration path
4. Update CHANGELOG

### Large Refactoring

For large refactoring:
1. Create an issue describing the plan
2. Break into smaller PRs
3. Each PR should be independently reviewable
4. Link PRs together in descriptions

## Tips for Faster Reviews

1. **Small PRs** - Easier to review
2. **Good description** - Explain the why
3. **Self-review first** - Catch obvious issues
4. **Respond quickly** - Keep momentum
5. **Be patient** - Reviewers are volunteers

## Getting Help

- **Stuck on something?** - Ask in the PR comments
- **Need guidance?** - Open a draft PR for discussion
- **Review taking long?** - Politely ping after a week

## Next Steps

- [Code Standards](code-standards.md) - Coding conventions
- [Testing Guide](testing.md) - Writing tests
- [Release Process](release-process.md) - How releases work
