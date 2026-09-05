using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace PDK.Runners.Docker;

/// <summary>
/// Supplies registry credentials for image pulls.
/// </summary>
internal interface IDockerRegistryAuthProvider
{
    /// <summary>
    /// Gets the credentials for a registry host, or null when none are configured.
    /// </summary>
    /// <param name="registryHost">The registry host (<c>docker.io</c> for Docker Hub).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<AuthConfig?> GetAuthConfigAsync(string registryHost, CancellationToken cancellationToken);
}

/// <summary>
/// Reads registry credentials from the Docker CLI configuration (<c>~/.docker/config.json</c> or
/// <c>$DOCKER_CONFIG/config.json</c>): inline <c>auths</c> entries (base64 <c>auth</c> or
/// <c>username</c>/<c>password</c>/<c>identitytoken</c>) and, when configured, credential helpers
/// (<c>credHelpers</c> / <c>credsStore</c>, invoked as <c>docker-credential-&lt;name&gt; get</c>).
/// </summary>
internal sealed class DockerConfigAuthProvider : IDockerRegistryAuthProvider
{
    /// <summary>
    /// Runs a credential helper and returns its JSON output, or null when the helper failed.
    /// </summary>
    internal delegate Task<string?> CredentialHelperRunner(string helperName, string serverAddress, CancellationToken cancellationToken);

    private static readonly string[] DockerHubAliases = { "docker.io", "index.docker.io", "registry-1.docker.io" };
    private static readonly TimeSpan HelperTimeout = TimeSpan.FromSeconds(10);

    private readonly IDockerHostEnvironment _environment;
    private readonly ILogger? _logger;
    private readonly CredentialHelperRunner _helperRunner;

    public DockerConfigAuthProvider(
        IDockerHostEnvironment environment,
        ILogger? logger = null,
        CredentialHelperRunner? helperRunner = null)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger;
        _helperRunner = helperRunner ?? RunCredentialHelperAsync;
    }

    /// <inheritdoc/>
    public async Task<AuthConfig?> GetAuthConfigAsync(string registryHost, CancellationToken cancellationToken)
    {
        var configPath = GetConfigPath();
        if (!_environment.FileExists(configPath))
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(_environment.ReadAllText(configPath));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger?.LogDebug(ex, "Could not read Docker config {Path}: {Message}", configPath, ex.Message);
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var isDockerHub = IsDockerHub(registryHost);

            // Inline credentials win over helpers when they contain something usable.
            if (root.TryGetProperty("auths", out var auths) && auths.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in auths.EnumerateObject())
                {
                    if (!KeyMatches(entry.Name, registryHost, isDockerHub))
                    {
                        continue;
                    }

                    var auth = ParseAuthEntry(entry.Value, entry.Name);
                    if (auth != null)
                    {
                        _logger?.LogDebug("Using credentials from Docker config for {Registry}", registryHost);
                        return auth;
                    }
                }
            }

            string? helper = null;
            if (root.TryGetProperty("credHelpers", out var helpers) && helpers.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in helpers.EnumerateObject())
                {
                    if (KeyMatches(entry.Name, registryHost, isDockerHub) && entry.Value.ValueKind == JsonValueKind.String)
                    {
                        helper = entry.Value.GetString();
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(helper) &&
                root.TryGetProperty("credsStore", out var store) &&
                store.ValueKind == JsonValueKind.String)
            {
                helper = store.GetString();
            }

            if (string.IsNullOrEmpty(helper))
            {
                return null;
            }

            var serverAddress = isDockerHub ? "https://index.docker.io/v1/" : registryHost;
            try
            {
                var json = await _helperRunner(helper, serverAddress, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                var auth = ParseHelperOutput(json, serverAddress);
                if (auth != null)
                {
                    _logger?.LogDebug("Using credentials from credential helper '{Helper}' for {Registry}", helper, registryHost);
                }

                return auth;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Credential helper '{Helper}' failed for {Registry}: {Message}", helper, registryHost, ex.Message);
                return null;
            }
        }
    }

    private string GetConfigPath()
    {
        var configDir = _environment.GetEnvironmentVariable("DOCKER_CONFIG");
        if (string.IsNullOrWhiteSpace(configDir))
        {
            configDir = Path.Combine(_environment.HomeDirectory, ".docker");
        }

        return Path.Combine(configDir, "config.json");
    }

    private static bool IsDockerHub(string registryHost)
    {
        var normalized = NormalizeKey(registryHost);
        return DockerHubAliases.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    private static bool KeyMatches(string key, string registryHost, bool isDockerHub)
    {
        var normalizedKey = NormalizeKey(key);
        if (isDockerHub)
        {
            return DockerHubAliases.Contains(normalizedKey, StringComparer.OrdinalIgnoreCase);
        }

        return string.Equals(normalizedKey, NormalizeKey(registryHost), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes a config key or host: strips the scheme, a trailing <c>/v1/</c> and trailing slashes.
    /// </summary>
    internal static string NormalizeKey(string key)
    {
        var value = key.Trim();
        var schemeIndex = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex >= 0)
        {
            value = value[(schemeIndex + 3)..];
        }

        value = value.TrimEnd('/');
        if (value.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^3];
        }

        return value.TrimEnd('/');
    }

    private static AuthConfig? ParseAuthEntry(JsonElement entry, string serverAddress)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? username = GetString(entry, "username");
        string? password = GetString(entry, "password");
        var identityToken = GetString(entry, "identitytoken");
        var registryToken = GetString(entry, "registrytoken");

        var encoded = GetString(entry, "auth");
        if (!string.IsNullOrEmpty(encoded))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                var separator = decoded.IndexOf(':');
                if (separator > 0)
                {
                    username ??= decoded[..separator];
                    password ??= decoded[(separator + 1)..];
                }
            }
            catch (FormatException)
            {
                // Ignore malformed auth values and fall back to explicit fields.
            }
        }

        if (string.IsNullOrEmpty(identityToken) && string.IsNullOrEmpty(registryToken) &&
            (string.IsNullOrEmpty(username) || password == null))
        {
            return null;
        }

        return new AuthConfig
        {
            Username = username,
            Password = password,
            IdentityToken = identityToken,
            RegistryToken = registryToken,
            ServerAddress = serverAddress
        };
    }

    private static AuthConfig? ParseHelperOutput(string json, string serverAddress)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var username = GetString(root, "Username");
        var secret = GetString(root, "Secret");
        if (string.IsNullOrEmpty(secret))
        {
            return null;
        }

        var server = GetString(root, "ServerURL");
        if (string.IsNullOrEmpty(server))
        {
            server = serverAddress;
        }

        if (string.Equals(username, "<token>", StringComparison.Ordinal))
        {
            return new AuthConfig { IdentityToken = secret, ServerAddress = server };
        }

        return new AuthConfig { Username = username, Password = secret, ServerAddress = server };
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private async Task<string?> RunCredentialHelperAsync(string helperName, string serverAddress, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = $"docker-credential-{helperName}",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("get");

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        await process.StandardInput.WriteAsync(serverAddress).ConfigureAwait(false);
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(HelperTimeout);
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            _logger?.LogDebug("Credential helper {Helper} timed out", startInfo.FileName);
            return null;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            _logger?.LogDebug("Credential helper {Helper} exited with {ExitCode}: {Error}", startInfo.FileName, process.ExitCode, stderr.Trim());
            return null;
        }

        return stdout;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Process already gone.
        }
    }
}
