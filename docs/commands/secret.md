# pdk secret

Manage locally stored secrets for pipeline execution.

## Syntax

```bash
pdk secret <subcommand> [options]
```

## Description

The `secret` command manages secrets stored locally on your machine. These secrets are:

- Encrypted at rest using AES-256
- Tied to your machine (cannot be transferred)
- Automatically masked in pipeline output
- Available as environment variables during pipeline execution

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
| `name` | Yes | Secret name (e.g., `API_KEY`, `DOCKER_PASSWORD`) |

### Options

| Option | Type | Description |
|--------|------|-------------|
| `--value <value>` | string | Secret value (visible in process list!) |
| `--stdin` | flag | Read value from standard input |

### Input Methods

**Interactive (Recommended)**

The safest method - value is not visible:

```bash
pdk secret set API_KEY
Enter secret value: ********
Secret 'API_KEY' stored successfully.
```

**From Standard Input**

Useful for scripts and automation:

```bash
echo "my-secret-value" | pdk secret set API_KEY --stdin
```

Or from a file:

```bash
cat secrets/api-key.txt | pdk secret set API_KEY --stdin
```

**Direct Value (Not Recommended)**

The value is visible in process lists and shell history:

```bash
pdk secret set API_KEY --value "my-secret-value"
# Warning: Secret value may be visible in process list
```

### Examples

```bash
# Interactive input (recommended)
pdk secret set GITHUB_TOKEN

# From stdin
echo "$MY_SECRET" | pdk secret set GITHUB_TOKEN --stdin

# From file
pdk secret set SSH_KEY --stdin < ~/.ssh/deploy_key

# Direct value (use with caution)
pdk secret set API_KEY --value "abc123"
```

---

## pdk secret list

List all stored secret names.

### Syntax

```bash
pdk secret list
```

### Output

```bash
pdk secret list
```

```
Stored secrets:
  - API_KEY
  - DOCKER_PASSWORD
  - GITHUB_TOKEN
  - NPM_TOKEN

4 secrets stored.
```

Note: Secret values are never displayed, only names.

---

## pdk secret delete

Remove a stored secret.

### Syntax

```bash
pdk secret delete <name>
```

### Arguments

| Argument | Required | Description |
|----------|----------|-------------|
| `name` | Yes | Secret name to delete |

### Examples

```bash
# Delete a secret
pdk secret delete API_KEY
Secret 'API_KEY' deleted.

# Try to delete non-existent secret
pdk secret delete UNKNOWN_SECRET
Error: Secret 'UNKNOWN_SECRET' not found.
```

---

## Using Secrets in Pipelines

Secrets are automatically available as environment variables during pipeline execution:

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
```

## Secret Storage

Secrets are stored in:

| Platform | Location |
|----------|----------|
| Windows | `%APPDATA%\PDK\secrets\` |
| macOS | `~/Library/Application Support/PDK/secrets/` |
| Linux | `~/.config/pdk/secrets/` |

**Security notes:**

- Secrets are encrypted using AES-256 with a machine-specific key
- Encrypted files cannot be decrypted on other machines
- The encryption key is derived from machine-specific identifiers
- Secrets are loaded into memory only when needed

## Secret Masking

PDK automatically masks secret values in output:

```
Step: Deploy
  Calling API with key: ***
  Response: 200 OK
```

Masking works for:

- Console output
- Log files (text and JSON)
- Error messages

To disable masking (for debugging only):

```bash
pdk run --no-redact
```

**Warning:** Using `--no-redact` may expose secrets in output.

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
| 1 | Error (secret not found, storage error) |
| 2 | Invalid arguments |

## See Also

- [Secrets Configuration](../configuration/secrets.md)
- [Variables](../configuration/variables.md)
- [pdk run](run.md)
