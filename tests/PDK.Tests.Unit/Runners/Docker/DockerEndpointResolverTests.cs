using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using PDK.Runners.Docker;

namespace PDK.Tests.Unit.Runners.Docker;

public class DockerEndpointResolverTests
{
    private readonly FakeDockerHostEnvironment _env = new();

    private string ConfigDir => U(_env.HomeDirectory, ".docker");

    private void AddContext(string name, string host)
    {
        var meta = U(ConfigDir, "contexts", "meta", DockerEndpointResolver.GetContextDirectoryName(name), "meta.json");
        _env.FileContents[meta] = "{\"Name\":\"" + name + "\",\"Metadata\":{},\"Endpoints\":{\"docker\":{\"Host\":\"" + host + "\",\"SkipTLSVerify\":false}}}";
    }

    [Fact]
    public void Resolve_DockerHostUnix_IsUsedFirst()
    {
        _env.Variables["DOCKER_HOST"] = "unix:///custom/docker.sock";
        _env.Files.Add("/var/run/docker.sock");

        var endpoint = DockerEndpointResolver.Resolve(_env);

        endpoint.Uri.Should().Be(new Uri("unix:///custom/docker.sock"));
        endpoint.SocketPath.Should().Be("/custom/docker.sock");
        endpoint.Source.Should().Contain("DOCKER_HOST");
    }

    [Fact]
    public void Resolve_DockerHostTcp_BecomesHttp()
    {
        _env.Variables["DOCKER_HOST"] = "tcp://10.0.0.5:2375";

        var endpoint = DockerEndpointResolver.Resolve(_env);

        endpoint.Uri.Scheme.Should().Be("http");
        endpoint.Uri.Host.Should().Be("10.0.0.5");
        endpoint.Uri.Port.Should().Be(2375);
        endpoint.IsLocal.Should().BeFalse();
    }

    [Fact]
    public void Resolve_DockerHostTcpWithoutPort_UsesDefaultPort()
    {
        _env.Variables["DOCKER_HOST"] = "tcp://docker.local";

        var endpoint = DockerEndpointResolver.Resolve(_env);

        endpoint.Uri.Port.Should().Be(2375);
    }

    [Fact]
    public void Resolve_DockerHostTcpWithTlsVerify_BecomesHttps()
    {
        _env.Variables["DOCKER_HOST"] = "tcp://docker.local";
        _env.Variables["DOCKER_TLS_VERIFY"] = "1";

        var endpoint = DockerEndpointResolver.Resolve(_env);

        endpoint.Uri.Scheme.Should().Be("https");
        endpoint.Uri.Port.Should().Be(2376);
    }

    [Theory]
    [InlineData("npipe:////./pipe/docker_engine")]
    [InlineData("npipe://./pipe/docker_engine")]
    public void Resolve_DockerHostNamedPipe_IsNormalized(string value)
    {
        _env.Variables["DOCKER_HOST"] = value;

        var endpoint = DockerEndpointResolver.Resolve(_env);

        endpoint.Uri.ToString().Should().Be("npipe://./pipe/docker_engine");
        endpoint.IsNamedPipe.Should().BeTrue();
    }

    [Fact]
    public void Resolve_DockerHostBarePath_BecomesUnixSocket()
    {
        _env.Variables["DOCKER_HOST"] = "/run/user/1000/docker.sock";

        var endpoint = DockerEndpointResolver.Resolve(_env);

        endpoint.Uri.Should().Be(new Uri("unix:///run/user/1000/docker.sock"));
    }

    [Fact]
    public void Resolve_DockerHostSsh_IsSkippedWithReason()
    {
        _env.Variables["DOCKER_HOST"] = "ssh://user@remote";
        _env.Files.Add("/var/run/docker.sock");

        var endpoint = DockerEndpointResolver.Resolve(_env);

        endpoint.Uri.Should().Be(new Uri("unix:///var/run/docker.sock"));
        endpoint.SearchedPaths.Should().Contain(p => p.Contains("DOCKER_HOST=ssh://user@remote") && p.Contains("not supported"));
    }

    [Fact]
    public void Resolve_DockerContextEnvironmentVariable_UsesContextEndpoint()
    {
        _env.Variables["DOCKER_CONTEXT"] = "colima";
        AddContext("colima", "unix:///home/tester/.colima/default/docker.sock");
        _env.Files.Add("/var/run/docker.sock");

        var endpoint = DockerEndpointResolver.Resolve(_env);

        endpoint.Uri.Should().Be(new Uri("unix:///home/tester/.colima/default/docker.sock"));
        endpoint.Source.Should().Be("Docker context 'colima'");
    }

    [Fact]
    public void Resolve_CurrentContextInConfig_UsesContextEndpoint()
    {
        _env.FileContents[U(ConfigDir, "config.json")] = "{\"currentContext\":\"desktop-linux\"}";
        AddContext("desktop-linux", "unix:///home/tester/.docker/desktop/docker.sock");

        var endpoint = DockerEndpointResolver.Resolve(_env);

        endpoint.Uri.Should().Be(new Uri("unix:///home/tester/.docker/desktop/docker.sock"));
        endpoint.Source.Should().Contain("desktop-linux");
    }

    [Fact]
    public void Resolve_DockerConfigDirectoryOverride_IsHonoured()
    {
        _env.Variables["DOCKER_CONFIG"] = "/etc/docker-cli";
        _env.FileContents["/etc/docker-cli/config.json"] = "{\"currentContext\":\"remote\"}";
        var meta = U("/etc/docker-cli", "contexts", "meta", DockerEndpointResolver.GetContextDirectoryName("remote"), "meta.json");
        _env.FileContents[meta] = "{\"Endpoints\":{\"docker\":{\"Host\":\"tcp://build-host:2376\"}}}";

        var endpoint = DockerEndpointResolver.Resolve(_env);

        endpoint.Uri.Host.Should().Be("build-host");
        endpoint.Uri.Port.Should().Be(2376);
        endpoint.Source.Should().Contain("remote");
    }

    [Fact]
    public void Resolve_DefaultContext_FallsThroughToSocketSearch()
    {
        _env.FileContents[U(ConfigDir, "config.json")] = "{\"currentContext\":\"default\"}";
        _env.Files.Add(U(_env.HomeDirectory, ".docker", "run", "docker.sock"));

        var endpoint = DockerEndpointResolver.Resolve(_env);

        endpoint.SocketPath.Should().Be(U(_env.HomeDirectory, ".docker", "run", "docker.sock"));
    }

    [Fact]
    public void Resolve_ContextMetadataMissing_RecordsAndFallsThrough()
    {
        _env.Variables["DOCKER_CONTEXT"] = "ghost";

        var endpoint = DockerEndpointResolver.Resolve(_env);

        endpoint.Uri.Should().Be(new Uri("unix:///var/run/docker.sock"));
        endpoint.SearchedPaths.Should().Contain(p => p.Contains("ghost"));
    }

    [Fact]
    public void Resolve_InvalidConfigJson_IsIgnored()
    {
        _env.FileContents[U(ConfigDir, "config.json")] = "{ not json";
        _env.Files.Add("/var/run/docker.sock");

        var endpoint = DockerEndpointResolver.Resolve(_env);

        endpoint.Uri.Should().Be(new Uri("unix:///var/run/docker.sock"));
    }

    [Fact]
    public void Resolve_ProbesWellKnownSocketsInOrder()
    {
        _env.Variables["XDG_RUNTIME_DIR"] = "/run/user/1000";
        _env.Files.Add(U(_env.HomeDirectory, ".orbstack", "run", "docker.sock"));
        _env.Files.Add("/run/podman/podman.sock");

        var endpoint = DockerEndpointResolver.Resolve(_env);

        endpoint.SocketPath.Should().Be(U(_env.HomeDirectory, ".orbstack", "run", "docker.sock"));
        endpoint.Source.Should().StartWith("socket ");
        endpoint.SearchedPaths.Should().ContainInOrder(
            "/var/run/docker.sock",
            U("/run/user/1000", "docker.sock"),
            U(_env.HomeDirectory, ".docker", "run", "docker.sock"));
    }

    [Fact]
    public void Resolve_RootlessDockerSocket_IsFound()
    {
        _env.Variables["XDG_RUNTIME_DIR"] = "/run/user/1000";
        _env.Files.Add("/run/user/1000/docker.sock");

        var endpoint = DockerEndpointResolver.Resolve(_env);

        endpoint.SocketPath.Should().Be(U("/run/user/1000", "docker.sock"));
    }

    [Fact]
    public void Resolve_PodmanSocket_IsFound()
    {
        _env.Variables["XDG_RUNTIME_DIR"] = "/run/user/1000";
        _env.Files.Add(U("/run/user/1000", "podman", "podman.sock"));

        var endpoint = DockerEndpointResolver.Resolve(_env);

        endpoint.SocketPath.Should().Be(U("/run/user/1000", "podman", "podman.sock"));
    }

    [Fact]
    public void Resolve_NoSocketFound_ReturnsDefaultWithSearchedPaths()
    {
        var endpoint = DockerEndpointResolver.Resolve(_env);

        endpoint.Uri.Should().Be(new Uri("unix:///var/run/docker.sock"));
        endpoint.Source.Should().Contain("no Docker socket found");
        endpoint.SearchedPaths.Should().Contain("/var/run/docker.sock");
        endpoint.SearchedPaths.Should().Contain(U(_env.HomeDirectory, ".colima", "default", "docker.sock"));
        endpoint.SearchedPaths.Should().Contain("/run/podman/podman.sock");
    }

    [Fact]
    public void Resolve_Windows_ReturnsNamedPipe()
    {
        _env.IsLinux = false;
        _env.IsWindows = true;

        var endpoint = DockerEndpointResolver.Resolve(_env);

        endpoint.Uri.ToString().Should().Be("npipe://./pipe/docker_engine");
        endpoint.IsNamedPipe.Should().BeTrue();
    }

    [Fact]
    public void Resolve_WindowsWithDockerHost_UsesDockerHost()
    {
        _env.IsLinux = false;
        _env.IsWindows = true;
        _env.Variables["DOCKER_HOST"] = "tcp://127.0.0.1:2375";

        var endpoint = DockerEndpointResolver.Resolve(_env);

        endpoint.Uri.Scheme.Should().Be("http");
        endpoint.Uri.Port.Should().Be(2375);
    }

    [Fact]
    public void GetContextDirectoryName_IsSha256OfName()
    {
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("desktop-linux"))).ToLowerInvariant();

        DockerEndpointResolver.GetContextDirectoryName("desktop-linux").Should().Be(expected);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("ftp://x", false)]
    [InlineData("unix://", false)]
    [InlineData("tcp://:2375", false)]
    [InlineData("localhost:2375", true)]
    public void TryParseEndpoint_RejectsUnsupportedValues(string value, bool expected)
    {
        var result = DockerEndpointResolver.TryParseEndpoint(value, _env, out _, out var problem);

        result.Should().Be(expected);
        if (!expected)
        {
            problem.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public void DockerConfig_DockerSocketUri_UsesResolver()
    {
        var uri = new DockerConfig().DockerSocketUri;

        uri.Should().NotBeNull();
        uri.Scheme.Should().BeOneOf("unix", "npipe", "http", "https");
    }

    [Fact]
    public void Resolve_RealEnvironment_ReturnsEndpoint()
    {
        var endpoint = DockerEndpointResolver.Resolve();

        endpoint.Should().NotBeNull();
        endpoint.Source.Should().NotBeNullOrEmpty();
    }

    /// <summary>Joins Unix path segments with '/', independent of the host that runs the tests.</summary>
    private static string U(params string[] parts) => string.Join("/", parts.Select((p, i) => i == 0 ? p.TrimEnd('/') : p.Trim('/')));
}
