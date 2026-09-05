# PDK Secrets Guide

## Overview

PDK provides secure secret management with encryption at rest, automatic output masking, and CLI
commands for secret lifecycle management.

## Security Model

- **Encryption at rest**: secrets are stored encrypted in `~/.pdk/secrets.json` (format version 2.0)
- **AES-256-GCM** with a random key stored in `~/.pdk/secret.key`; both files are created with
  owner-only permissions (mode 0600 on Unix, in a `~/.pdk` directory created with mode 0700). On
  Windows the key file content is additionally protected with DPAPI (current user scope).
- **Migration**: entries written by earlier PDK versions (AES-256-CBC with a machine-derived key on
  macOS/Linux, DPAPI on Windows) are re-encrypted with the current format the first time they are
  read; entries that cannot be decrypted are kept and reported as `(unreadable)` by `pdk secret list`.
- **Output masking**: secret values are replaced with `***` in step output, logs and error messages
- **Concurrency**: updates to the store are serialised with a lock file and written atomically

## Setting Secrets

### Interactive (Recommended)

```bash
pdk secret set API_KEY
# Prompts: Enter value for API_KEY: [input hidden]
```

### From stdin (For CI/Scripting)

```bash
echo "my-secret-value" | pdk secret set API_KEY --stdin
```

### From Environment Variable

```bash
export PDK_SECRET_API_KEY="my-secret-value"
pdk run --file pipeline.yml
# API_KEY is available to steps as $API_KEY / ${{ secrets.API_KEY }} / $(API_KEY), masked in output
```

### Via CLI --value (Not Recommended)

```bash
pdk secret set API_KEY --value my-secret-value
# WARNING: Value visible in process list
```

### Via Run Command (Not Recommended)

```bash
pdk run --secret API_KEY=my-secret-value
# WARNING: Value visible in process list; overrides a stored secret with the same name for this run
```

## Managing Secrets

### List Secret Names

```bash
pdk secret list
# API_KEY
# DB_PASSWORD
# OLD_TOKEN (unreadable: cannot be decrypted with the current key; set it again)
```

`No secrets stored` is printed when the store is empty.

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

Secrets are exported to every step by name and are available in expressions:

```yaml
steps:
  - name: Deploy
    run: |
      curl -H "Authorization: Bearer $API_KEY" https://api.example.com/deploy
    env:
      TOKEN: ${{ secrets.API_KEY }}   # also works
```

Azure Pipelines: `$(API_KEY)`, `variables['API_KEY']` or `$API_KEY`.

Output shows:
```
+ curl -H "Authorization: Bearer ***" https://api.example.com/deploy
```

## Secret Detection

PDK warns when a `--var` value looks like a secret based on its name:
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
- Step output (streamed and captured, including the error context of failed steps)
- Console log output, `--log-file` and `--log-json` files and the default log
- Error messages
- Dry-run output (`***MASKED***` for every variable the resolver knows as a secret)

Masking rules:
- Registered values (stored secrets, `PDK_SECRET_*`, `--secret`) are replaced with `***`,
  case-insensitively; longer values are processed first to handle overlaps
- Each non-trivial line of a multi-line secret (for example a PEM key) is masked on its own
- URL-encoded, JSON-escaped and base64 (standard and URL-safe) forms of a secret are masked too
- Values marked with `::add-mask::` (GitHub) or `isSecret=true` / `##vso[task.setsecret]` (Azure)
  are masked in the output of the following steps
- Log output additionally masks common patterns without registration: `password=...`,
  `token=...`, `api_key=...`, URL credentials (`https://user:***@host`) and `Authorization: Bearer ...`
  headers
- Values shorter than 3 characters are not masked

### Disabling Masking

For debugging only (use with extreme caution):

```bash
pdk run --no-redact
```

`--no-redact` disables redaction in the logging pipeline (console mirror, log files, default log).
Values registered as secrets are still replaced with `***` in captured step output.

## Storage Location

| File | Purpose | Permissions |
|------|---------|-------------|
| `~/.pdk/secrets.json` | Encrypted secret entries (name, ciphertext, algorithm, timestamps) | 0600 |
| `~/.pdk/secret.key` | Random 256-bit key (DPAPI-wrapped on Windows) | 0600 |
| `~/.pdk/secrets.json.lock`, `~/.pdk/secret.key.lock` | Cross-process locks | 0600 |

The same locations are used on Windows, macOS and Linux (`~` is the user profile directory).

## Best Practices

1. **Never commit secrets**: keep `~/.pdk` out of any repository

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
   pdk run --secret TOKEN=example-value
   ```

4. **Rotate secrets regularly**: Use `pdk secret set NAME` to update

5. **Use separate secrets for environments**:
   - `STAGING_API_KEY`
   - `PRODUCTION_API_KEY`

6. **Verify masking**: Check your output doesn't contain secret values

## Troubleshooting

### "(unreadable)" in `pdk secret list` / "Secret decryption failed"

The entry was encrypted with a different key: the store was copied from another machine or user
account without its `secret.key`, or the key file was replaced. Set the secret again:
```bash
pdk secret set API_KEY
```

### Secret Not Masked

Ensure the secret is:
1. At least 3 characters long
2. Registered via `pdk secret set`, `--secret`, or `PDK_SECRET_*`
3. Not disabled with `--no-redact`

### Secret Not Found

1. Verify the secret exists: `pdk secret list`
2. Check the secret name (case-sensitive; names match `^[A-Za-z_][A-Za-z0-9_]*$`)
3. A step that references a missing secret gets an empty value; `pdk secret delete` of a missing
   name exits with code 1

### Permission Denied

On Unix systems, check file permissions:
```bash
ls -la ~/.pdk
# secrets.json and secret.key should be -rw------- (0600)
chmod 600 ~/.pdk/secrets.json ~/.pdk/secret.key
```

## Security Considerations

1. **Secrets are bound to the key file**: copying `secrets.json` alone to another machine or user
   yields unreadable entries; on Windows the key is DPAPI-protected and cannot be moved to another
   user at all
2. **Process memory**: Secrets exist in memory during execution
3. **Log files**: Secrets are masked but verify your logging configuration
4. **CI/CD**: Use environment variables (`PDK_SECRET_*`) for injection

## See Also

- [pdk secret Command](../commands/secret.md)
- [Variables Guide](variables.md)
- [Expressions](../expressions.md)
- [Logging](logging.md)
