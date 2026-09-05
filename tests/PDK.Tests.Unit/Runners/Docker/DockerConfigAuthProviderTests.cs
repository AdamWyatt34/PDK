using System.Text;
using FluentAssertions;
using PDK.Runners.Docker;

namespace PDK.Tests.Unit.Runners.Docker;

public class DockerConfigAuthProviderTests
{
    private readonly FakeDockerHostEnvironment _env = new();

    private string ConfigPath => Path.Combine(_env.HomeDirectory, ".docker", "config.json");

    private static string Base64(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    [Fact]
    public async Task NoConfigFile_ReturnsNull()
    {
        var provider = new DockerConfigAuthProvider(_env);

        var auth = await provider.GetAuthConfigAsync("ghcr.io", CancellationToken.None);

        auth.Should().BeNull();
    }

    [Fact]
    public async Task InlineBase64Auth_IsDecoded()
    {
        _env.FileContents[ConfigPath] = "{\"auths\":{\"ghcr.io\":{\"auth\":\"" + Base64("alice:pa:ss") + "\"}}}";
        var provider = new DockerConfigAuthProvider(_env);

        var auth = await provider.GetAuthConfigAsync("ghcr.io", CancellationToken.None);

        auth.Should().NotBeNull();
        auth!.Username.Should().Be("alice");
        auth.Password.Should().Be("pa:ss");
        auth.ServerAddress.Should().Be("ghcr.io");
    }

    [Fact]
    public async Task UsernamePasswordFields_AreUsed()
    {
        _env.FileContents[ConfigPath] = "{\"auths\":{\"https://registry.example.com\":{\"username\":\"bob\",\"password\":\"secret\"}}}";
        var provider = new DockerConfigAuthProvider(_env);

        var auth = await provider.GetAuthConfigAsync("registry.example.com", CancellationToken.None);

        auth!.Username.Should().Be("bob");
        auth.Password.Should().Be("secret");
    }

    [Fact]
    public async Task IdentityToken_IsUsed()
    {
        _env.FileContents[ConfigPath] = "{\"auths\":{\"registry.example.com\":{\"identitytoken\":\"tok\"}}}";
        var provider = new DockerConfigAuthProvider(_env);

        var auth = await provider.GetAuthConfigAsync("registry.example.com", CancellationToken.None);

        auth!.IdentityToken.Should().Be("tok");
    }

    [Theory]
    [InlineData("https://index.docker.io/v1/")]
    [InlineData("index.docker.io")]
    [InlineData("docker.io")]
    [InlineData("registry-1.docker.io")]
    public async Task DockerHubAliases_MatchDockerIo(string key)
    {
        _env.FileContents[ConfigPath] = "{\"auths\":{\"" + key + "\":{\"auth\":\"" + Base64("hub:pw") + "\"}}}";
        var provider = new DockerConfigAuthProvider(_env);

        var auth = await provider.GetAuthConfigAsync("docker.io", CancellationToken.None);

        auth.Should().NotBeNull();
        auth!.Username.Should().Be("hub");
    }

    [Fact]
    public async Task OtherRegistryEntry_DoesNotMatch()
    {
        _env.FileContents[ConfigPath] = "{\"auths\":{\"ghcr.io\":{\"auth\":\"" + Base64("a:b") + "\"}}}";
        var provider = new DockerConfigAuthProvider(_env);

        var auth = await provider.GetAuthConfigAsync("quay.io", CancellationToken.None);

        auth.Should().BeNull();
    }

    [Fact]
    public async Task EmptyAuthWithCredsStore_UsesCredentialHelper()
    {
        _env.FileContents[ConfigPath] = "{\"auths\":{\"ghcr.io\":{}},\"credsStore\":\"desktop\"}";
        string? helperUsed = null;
        string? serverUsed = null;
        var provider = new DockerConfigAuthProvider(_env, null, (helper, server, _) =>
        {
            helperUsed = helper;
            serverUsed = server;
            return Task.FromResult<string?>("{\"ServerURL\":\"ghcr.io\",\"Username\":\"carol\",\"Secret\":\"pat\"}");
        });

        var auth = await provider.GetAuthConfigAsync("ghcr.io", CancellationToken.None);

        helperUsed.Should().Be("desktop");
        serverUsed.Should().Be("ghcr.io");
        auth!.Username.Should().Be("carol");
        auth.Password.Should().Be("pat");
    }

    [Fact]
    public async Task CredHelpersForRegistry_TakesPrecedenceOverCredsStore()
    {
        _env.FileContents[ConfigPath] = "{\"credHelpers\":{\"123.dkr.ecr.eu-west-1.amazonaws.com\":\"ecr-login\"},\"credsStore\":\"desktop\"}";
        string? helperUsed = null;
        var provider = new DockerConfigAuthProvider(_env, null, (helper, _, _) =>
        {
            helperUsed = helper;
            return Task.FromResult<string?>("{\"Username\":\"AWS\",\"Secret\":\"token\"}");
        });

        var auth = await provider.GetAuthConfigAsync("123.dkr.ecr.eu-west-1.amazonaws.com", CancellationToken.None);

        helperUsed.Should().Be("ecr-login");
        auth!.Username.Should().Be("AWS");
    }

    [Fact]
    public async Task HelperTokenUser_BecomesIdentityToken()
    {
        _env.FileContents[ConfigPath] = "{\"credsStore\":\"osxkeychain\"}";
        var provider = new DockerConfigAuthProvider(_env, null, (_, _, _) =>
            Task.FromResult<string?>("{\"Username\":\"<token>\",\"Secret\":\"id-token\"}"));

        var auth = await provider.GetAuthConfigAsync("docker.io", CancellationToken.None);

        auth!.IdentityToken.Should().Be("id-token");
        auth.Username.Should().BeNull();
    }

    [Fact]
    public async Task DockerHubHelperLookup_UsesIndexServerAddress()
    {
        _env.FileContents[ConfigPath] = "{\"credsStore\":\"desktop\"}";
        string? serverUsed = null;
        var provider = new DockerConfigAuthProvider(_env, null, (_, server, _) =>
        {
            serverUsed = server;
            return Task.FromResult<string?>(null);
        });

        await provider.GetAuthConfigAsync("docker.io", CancellationToken.None);

        serverUsed.Should().Be("https://index.docker.io/v1/");
    }

    [Fact]
    public async Task HelperFailure_ReturnsNull()
    {
        _env.FileContents[ConfigPath] = "{\"credsStore\":\"desktop\"}";
        var provider = new DockerConfigAuthProvider(_env, null, (_, _, _) => throw new InvalidOperationException("helper missing"));

        var auth = await provider.GetAuthConfigAsync("ghcr.io", CancellationToken.None);

        auth.Should().BeNull();
    }

    [Fact]
    public async Task InvalidJson_ReturnsNull()
    {
        _env.FileContents[ConfigPath] = "{ nope";
        var provider = new DockerConfigAuthProvider(_env);

        var auth = await provider.GetAuthConfigAsync("ghcr.io", CancellationToken.None);

        auth.Should().BeNull();
    }

    [Fact]
    public async Task DockerConfigEnvironmentVariable_OverridesConfigDirectory()
    {
        _env.Variables["DOCKER_CONFIG"] = "/etc/docker-cli";
        _env.FileContents["/etc/docker-cli/config.json"] = "{\"auths\":{\"ghcr.io\":{\"auth\":\"" + Base64("x:y") + "\"}}}";
        var provider = new DockerConfigAuthProvider(_env);

        var auth = await provider.GetAuthConfigAsync("ghcr.io", CancellationToken.None);

        auth!.Username.Should().Be("x");
    }

    [Theory]
    [InlineData("https://index.docker.io/v1/", "index.docker.io")]
    [InlineData("http://ghcr.io/", "ghcr.io")]
    [InlineData("registry.example.com:5000/v1", "registry.example.com:5000")]
    [InlineData("quay.io", "quay.io")]
    public void NormalizeKey_StripsSchemeAndVersionSuffix(string key, string expected)
    {
        DockerConfigAuthProvider.NormalizeKey(key).Should().Be(expected);
    }
}
