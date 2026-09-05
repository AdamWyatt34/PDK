using FluentAssertions;
using PDK.Runners.Docker;

namespace PDK.Tests.Unit.Runners.Docker;

public class ImageReferenceTests
{
    private const string Digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData("ubuntu", null, "ubuntu", null, null)]
    [InlineData("ubuntu:22.04", null, "ubuntu", "22.04", null)]
    [InlineData("node:18-alpine", null, "node", "18-alpine", null)]
    [InlineData("library/ubuntu:latest", null, "library/ubuntu", "latest", null)]
    [InlineData("mcr.microsoft.com/dotnet/sdk:8.0", "mcr.microsoft.com", "dotnet/sdk", "8.0", null)]
    [InlineData("localhost:5000/app:1.0", "localhost:5000", "app", "1.0", null)]
    [InlineData("localhost/app", "localhost", "app", null, null)]
    [InlineData("registry.example.com:8443/team/app", "registry.example.com:8443", "team/app", null, null)]
    [InlineData("myregistry/myimage", null, "myregistry/myimage", null, null)]
    [InlineData("ghcr.io/org/tool:v1.2.3", "ghcr.io", "org/tool", "v1.2.3", null)]
    [InlineData("UPPER/repo:1", "UPPER", "repo", "1", null)]
    public void TryParse_ParsesComponents(string value, string? registry, string repository, string? tag, string? digest)
    {
        ImageReference.TryParse(value, out var reference).Should().BeTrue();

        reference!.Registry.Should().Be(registry);
        reference.Repository.Should().Be(repository);
        reference.Tag.Should().Be(tag);
        reference.Digest.Should().Be(digest);
    }

    [Fact]
    public void TryParse_DigestReference()
    {
        ImageReference.TryParse("ubuntu@" + Digest, out var reference).Should().BeTrue();

        reference!.Name.Should().Be("ubuntu");
        reference.Tag.Should().BeNull();
        reference.Digest.Should().Be(Digest);
        reference.PullTag.Should().Be(Digest);
        reference.Canonical.Should().Be("ubuntu@" + Digest);
    }

    [Fact]
    public void TryParse_TagAndDigest()
    {
        ImageReference.TryParse("ghcr.io/org/app:1.0@" + Digest, out var reference).Should().BeTrue();

        reference!.Registry.Should().Be("ghcr.io");
        reference.Tag.Should().Be("1.0");
        reference.Digest.Should().Be(Digest);
        reference.PullTag.Should().Be(Digest);
    }

    [Fact]
    public void Properties_ForDockerHubImage()
    {
        var reference = ImageReference.Parse("ubuntu");

        reference.RegistryHost.Should().Be("docker.io");
        reference.PullTag.Should().Be("latest");
        reference.Canonical.Should().Be("ubuntu:latest");
        reference.Name.Should().Be("ubuntu");
    }

    [Fact]
    public void Properties_ForRegistryImage()
    {
        var reference = ImageReference.Parse("localhost:5000/app:1.0");

        reference.RegistryHost.Should().Be("localhost:5000");
        reference.Name.Should().Be("localhost:5000/app");
        reference.PullTag.Should().Be("1.0");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ubuntu")]
    [InlineData("ubuntu/Repo")]
    [InlineData("library/Ubuntu:22.04")]
    [InlineData("ubuntu::")]
    [InlineData("ubuntu:")]
    [InlineData("ubuntu:tag with space")]
    [InlineData("ubuntu@sha256:short")]
    [InlineData("ubuntu@notadigest")]
    [InlineData("-bad/name")]
    [InlineData("name/")]
    [InlineData("registry.example.com:notaport/app")]
    public void TryParse_RejectsInvalidReferences(string value)
    {
        ImageReference.TryParse(value, out _).Should().BeFalse();
    }

    [Fact]
    public void Parse_Invalid_ThrowsArgumentException()
    {
        Action act = () => ImageReference.Parse("Ubuntu::bad");

        act.Should().Throw<ArgumentException>().WithMessage("*not valid*");
    }
}
