# PDK Secrets Guide

## Overview

PDK provides secure secret management with encryption at rest, automatic output masking, and CLI commands for secret lifecycle management.

## Security Model

- **Encryption at rest**: Secrets stored encrypted on disk
- **Platform-specific encryption**:
  - Windows: DPAPI (Data Protection API)
  - macOS/Linux: AES-256-CBC with machine-derived key
- **Output masking**: Secret values replaced with `***` in all output
- **Memory clearing**: Plaintext cleared from memory after use

## Setting Secrets

### Interactive (Recommended)

```bash
pdk secret set API_KEY
# Prompts: Enter value for API_KEY: [input masked]
```

### From stdin (For CI/Scripting)

```bash
echo "my-secret-value" | pdk secret set API_KEY --stdin
```

### From Environment Variable

```bash
export PDK_SECRET_API_KEY="my-secret-value"
pdk run --file pipeline.yml
# API_KEY available as ${API_KEY}, masked in output
```

### Via CLI --value (Not Recommended)

```bash
pdk secret set API_KEY --value my-secret-value
# WARNING: Value visible in process list
```

### Via Run Command (Not Recommended)

```bash
pdk run --secret API_KEY=my-secret-value
# WARNING: Value visible in process list
```

## Managing Secrets

### List Secret Names

```bash
pdk secret list
# Output: API_KEY, DB_PASSWORD, GITHUB_TOKEN
```

### Delete a Secret

```bash
pdk secret delete API_KEY
```

### Update a Secret

```bash
pdk secret set API_KEY
# Prompts for new value, overwrites existing
```

## Using Secrets in Pipelines

Reference secrets like any variable:

```yaml
steps:
  - name: Deploy
    run: |
      curl -H "Authorization: Bearer ${API_KEY}" \
           https://api.example.com/deploy
```

Output shows:
```
[Deploy] + curl -H "Authorization: Bearer ***" https://api.example.com/deploy
```

## Secret Detection

PDK automatically detects variables that may contain secrets based on their names:
- password, passwd, pwd
- secret, token, key
- api_key, apikey, api-key
- auth, credential
- private, privatekey
- access_token, refresh_token
- bearer, certificate, cert

If detected, PDK warns:
```
Warning: Variable 'DB_PASSWORD' appears to contain a secret.
Recommendation: Use 'pdk secret set DB_PASSWORD' for secure storage.
```

## Secret Masking

Secret values are masked in:
- Console output
- Log files (text and JSON)
- Error messages
- Progress reports
- Execution summaries

Masking rules:
- Replace with `***`
- Case-insensitive matching
- Values < 3 characters are not masked (too short)
- Longer secrets are processed first to handle overlaps

### Disabling Masking

For debugging only (use with extreme caution):

```bash
pdk run --no-redact
```

## Storage Location

Secrets are stored in:

| Platform | Location |
|----------|----------|
| Windows | `%APPDATA%\PDK\secrets\` |
| macOS | `~/Library/Application Support/PDK/secrets/` |
| Linux | `~/.config/pdk/secrets/` |

File permissions:
- Windows: User-only access via ACLs
- Unix: Mode 0600 (owner read/write only)

## Best Practices

1. **Never commit secrets**: Add secret storage locations to `.gitignore`

2. **Use environment variables in CI**:
   ```yaml
   env:
     PDK_SECRET_DEPLOY_TOKEN: ${{ secrets.DEPLOY_TOKEN }}
   ```

3. **Prefer interactive input over CLI**:
   ```bash
   # Good - not visible
   pdk secret set TOKEN

   # Bad - visible in ps/Task Manager
   pdk run --secret TOKEN=abc123
   ```

4. **Rotate secrets regularly**: Use `pdk secret set NAME` to update

5. **Use separate secrets for environments**:
   - `STAGING_API_KEY`
   - `PRODUCTION_API_KEY`

6. **Verify masking**: Check your output doesn't contain secret values

## Troubleshooting

### "Secret cannot be decrypted"

Secrets encrypted on one machine cannot be decrypted on another (machine-specific keys). Re-set secrets on the new machine:
```bash
pdk secret set API_KEY
```

### Secret Not Masked

Ensure the secret is:
1. At least 3 characters long
2. Registered via `pdk secret set`, `--secret`, or `PDK_SECRET_*`
3. Loaded before pipeline execution

### Secret Not Found

1. Verify the secret exists: `pdk secret list`
2. Check the secret name (case-sensitive)
3. Ensure secrets are loaded in the pipeline

### Permission Denied

On Unix systems, check file permissions:
```bash
ls -la ~/.config/pdk/secrets/
# Should be: drwx------ (0700)
chmod 700 ~/.config/pdk/secrets/
```

## Security Considerations

1. **Secrets are machine-specific**: Cannot be transferred between machines
2. **Process memory**: Secrets exist in memory during execution
3. **Log files**: Secrets are masked but verify your logging configuration
4. **CI/CD**: Use environment variables (`PDK_SECRET_*`) for injection

## See Also

- [pdk secret Command](../commands/secret.md)
- [Variables Guide](variables.md)
- [Logging](logging.md)
