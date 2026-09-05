# pdk secret

Manage locally stored secrets for pipeline execution.

## Syntax

```bash
pdk secret <subcommand> [options]
```

## Description

The `secret` command manages secrets stored locally on your machine. These secrets are:

- Encrypted at rest with AES-256-GCM, using a random key kept in `~/.pdk/secret.key`
- Automatically masked in pipeline output and logs
- Exported to every step as environment variables and available in expressions
  (`${{ secrets.NAME }}`, `$(NAME)`)

## Subcommands

| Subcommand | Description |
|------------|-------------|
| `set` | Store a new secret |
| `list` | List stored secret names |
| `delete` | Remove a stored secret |

---

## pdk secret set

Store a secret value.

### Syntax

```bash
pdk secret set <name> [options]
```

### Arguments

| Argument | Required | Description |
|----------|----------|-------------|
| `name` | Yes | Secret name (letters, digits and underscores, not starting with a digit; e.g. `API_KEY`) |

### Options

| Option | Type | Description |
|--------|------|-------------|
| `--value <value>` | string | Secret value (visible in process list!) |
| `--stdin` | flag | Read value from standard input |

### Input Methods

**Interactive (Recommended)**

The safest method - the value is not echoed:

```bash
pdk secret set API_KEY
Enter value for API_KEY:
✓ Secret 'API_KEY' saved
```

**From Standard Input**

Useful for scripts and automation:

```bash
echo "my-secret-value" | pdk secret set API_KEY --stdin
```

Or from a file:

```bash
pdk secret set SSH_KEY --stdin < ~/.ssh/deploy_key
```

**Direct Value (Not Recommended)**

The value is visible in process lists and shell history:

```bash
pdk secret set API_KEY --value "my-secret-value"
# Warning: Value provided via CLI is visible in process list.
```

---

## pdk secret list

List all stored secret names, one per line.

### Syntax

```bash
pdk secret list
```

### Output

```
API_KEY
DOCKER_PASSWORD
OLD_TOKEN (unreadable: cannot be decrypted with the current key; set it again)
```

`No secrets stored` is printed when the store is empty. Secret values are never displayed.

---

## pdk secret delete

Remove a stored secret.

### Syntax

```bash
pdk secret delete <name>
```

### Examples

```bash
# Delete a secret
pdk secret delete API_KEY
✓ Secret 'API_KEY' deleted

# Try to delete non-existent secret (exit code 1)
pdk secret delete UNKNOWN_SECRET
Secret 'UNKNOWN_SECRET' not found
```

---

## Using Secrets in Pipelines

Secrets are automatically available as environment variables and in expressions during pipeline
execution:

```yaml
steps:
  - name: Deploy
    run: |
      curl -H "Authorization: Bearer $API_KEY" \
           https://api.example.com/deploy
    env:
      API_KEY: ${{ secrets.API_KEY }}
```

With PDK:

```bash
# Store the secret
pdk secret set API_KEY

# Run the pipeline - API_KEY is automatically available
pdk run --file .github/workflows/deploy.yml

# Override it for one run (visible in the process list)
pdk run --file .github/workflows/deploy.yml --secret API_KEY=other-value
```

`PDK_SECRET_<NAME>` environment variables are another way to supply secrets, for example in CI.

## Secret Storage

| File | Purpose |
|------|---------|
| `~/.pdk/secrets.json` | Encrypted entries (format version 2.0) |
| `~/.pdk/secret.key` | Random 256-bit key (DPAPI-protected on Windows) |

Both files are created with owner-only permissions (0600). Entries written by earlier PDK versions
are migrated to the current format on first read; entries that cannot be decrypted are reported as
`(unreadable)` by `pdk secret list` and can simply be set again.

## Secret Masking

PDK automatically masks secret values in output:

```
Step: Deploy
  Calling API with key: ***
  Response: 200 OK
```

Masking works for:

- Step output (streamed and captured)
- Console output and log files (text and JSON)
- Error messages
- Dry-run output (`***MASKED***`)

To disable masking in the log sinks (for debugging only):

```bash
pdk run --no-redact
```

**Warning:** Using `--no-redact` may expose secrets in logs.

## Best Practices

1. **Use interactive input** for manual secret entry
2. **Use stdin** for automation and scripts
3. **Avoid --value** as it exposes secrets in process lists
4. **Rotate secrets regularly** by using `pdk secret set` to overwrite
5. **Delete unused secrets** with `pdk secret delete`
6. **Don't commit secrets** to version control

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Error (secret not found, invalid name, storage error) |
| 2 | Invalid arguments |

## See Also

- [Secrets Configuration](../configuration/secrets.md)
- [Variables](../configuration/variables.md)
- [pdk run](run.md)
