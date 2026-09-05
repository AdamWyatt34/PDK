namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using PDK.Runners;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for <see cref="ScriptShellSupport"/>: shell name resolution, script wrapping and interpreter
/// arguments following the GitHub Actions shell templates.
/// </summary>
public class ScriptShellSupportTests
{
    [Theory]
    [InlineData("bash", "Bash")]
    [InlineData("BASH", "Bash")]
    [InlineData("/bin/bash", "Bash")]
    [InlineData("/usr/bin/bash -e {0}", "Bash")]
    [InlineData("bash --noprofile --norc -eo pipefail {0}", "Bash")]
    [InlineData("sh", "Sh")]
    [InlineData("/bin/sh -e {0}", "Sh")]
    [InlineData("pwsh", "Pwsh")]
    [InlineData("pwsh.exe", "Pwsh")]
    [InlineData("pwsh -command \". '{0}'\"", "Pwsh")]
    [InlineData("powershell", "PowerShell")]
    [InlineData("C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe", "PowerShell")]
    [InlineData("python", "Python")]
    [InlineData("python3", "Python")]
    [InlineData("py", "Python")]
    [InlineData("python {0}", "Python")]
    [InlineData("cmd", "Cmd")]
    [InlineData("cmd.exe", "Cmd")]
    [InlineData("C:\\Windows\\System32\\cmd.exe /D /E:ON /V:OFF /S /C \"{0}\"", "Cmd")]
    public void TryResolve_RecognizesNamesPathsAndTemplates(string shellName, string expected)
    {
        var resolved = ScriptShellSupport.TryResolve(shellName, OperatingSystemPlatform.Linux, out var shell, out var error);

        resolved.Should().BeTrue();
        shell.Should().Be(Enum.Parse<ScriptShell>(expected));
        error.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolve_EmptyShell_DefaultsToBashOnUnixAndCmdOnWindows(string? shellName)
    {
        ScriptShellSupport.TryResolve(shellName, OperatingSystemPlatform.Linux, out var linuxShell, out _).Should().BeTrue();
        ScriptShellSupport.TryResolve(shellName, OperatingSystemPlatform.MacOS, out var macShell, out _).Should().BeTrue();
        ScriptShellSupport.TryResolve(shellName, OperatingSystemPlatform.Windows, out var windowsShell, out _).Should().BeTrue();

        linuxShell.Should().Be(ScriptShell.Bash);
        macShell.Should().Be(ScriptShell.Bash);
        windowsShell.Should().Be(ScriptShell.Cmd);
    }

    [Theory]
    [InlineData("fish")]
    [InlineData("zsh -l {0}")]
    [InlineData("node")]
    public void TryResolve_UnsupportedShell_ReturnsFalseWithHelpfulError(string shellName)
    {
        var resolved = ScriptShellSupport.TryResolve(shellName, OperatingSystemPlatform.Linux, out _, out var error);

        resolved.Should().BeFalse();
        error.Should().Contain($"Unsupported shell '{shellName}'")
            .And.Contain("bash, sh, pwsh, powershell, python, cmd");
    }

    [Theory]
    [InlineData("Bash", "bash", ".sh")]
    [InlineData("Sh", "sh", ".sh")]
    [InlineData("Pwsh", "pwsh", ".ps1")]
    [InlineData("PowerShell", "powershell", ".ps1")]
    [InlineData("Python", "python", ".py")]
    [InlineData("Cmd", "cmd", ".cmd")]
    public void GetDisplayNameAndExtension_MatchTheShell(string shellName, string displayName, string extension)
    {
        var shell = Enum.Parse<ScriptShell>(shellName);
        ScriptShellSupport.GetDisplayName(shell).Should().Be(displayName);
        ScriptShellSupport.GetFileExtension(shell).Should().Be(extension);
    }

    [Fact]
    public void GetExecutableCandidates_ListsFallbacksInOrder()
    {
        ScriptShellSupport.GetExecutableCandidates(ScriptShell.Bash).Should().Equal("bash");
        ScriptShellSupport.GetExecutableCandidates(ScriptShell.Sh).Should().Equal("sh");
        ScriptShellSupport.GetExecutableCandidates(ScriptShell.Pwsh).Should().Equal("pwsh");
        ScriptShellSupport.GetExecutableCandidates(ScriptShell.PowerShell).Should().Equal("powershell", "pwsh");
        ScriptShellSupport.GetExecutableCandidates(ScriptShell.Python).Should().Equal("python3", "python");
        ScriptShellSupport.GetExecutableCandidates(ScriptShell.Cmd).Should().Equal("cmd.exe");
    }

    [Theory]
    [InlineData("PowerShell", "pwsh", "Pwsh")]
    [InlineData("PowerShell", "/usr/bin/pwsh", "Pwsh")]
    [InlineData("PowerShell", "pwsh.exe", "Pwsh")]
    [InlineData("PowerShell", "powershell.exe", "PowerShell")]
    [InlineData("Python", "python", "Python")]
    [InlineData("Bash", "bash", "Bash")]
    public void ShellForExecutable_MapsFallbackExecutablesToTheirRules(string requested, string executable, string expected)
    {
        ScriptShellSupport.ShellForExecutable(Enum.Parse<ScriptShell>(requested), executable)
            .Should().Be(Enum.Parse<ScriptShell>(expected));
    }

    [Fact]
    public void PrepareContent_Bash_NormalizesLineEndingsAndAddsTrailingNewline()
    {
        var content = ScriptShellSupport.PrepareContent(ScriptShell.Bash, "echo one\r\necho two\rtail");

        content.Should().Be("echo one\necho two\ntail\n");
    }

    [Fact]
    public void PrepareContent_Sh_KeepsExistingTrailingNewline()
    {
        ScriptShellSupport.PrepareContent(ScriptShell.Sh, "echo hi\n").Should().Be("echo hi\n");
    }

    [Theory]
    [InlineData("Pwsh")]
    [InlineData("PowerShell")]
    public void PrepareContent_PowerShell_WrapsWithErrorPreferenceAndExitCode(string shellName)
    {
        var content = ScriptShellSupport.PrepareContent(Enum.Parse<ScriptShell>(shellName), "Write-Host 'hi'\r\nnative-tool\r\n");

        content.Should().Be(
            "$ErrorActionPreference = 'stop'\n" +
            "Write-Host 'hi'\nnative-tool\n" +
            "if ((Test-Path -LiteralPath variable:\\LASTEXITCODE)) { exit $LASTEXITCODE }\n");
    }

    [Fact]
    public void PrepareContent_Cmd_UsesCrLfLineEndings()
    {
        var content = ScriptShellSupport.PrepareContent(ScriptShell.Cmd, "echo one\necho two");

        content.Should().Be("echo one\r\necho two\r\n");
    }

    [Fact]
    public void BuildInterpreterArguments_FollowTheGitHubTemplates()
    {
        ScriptShellSupport.BuildInterpreterArguments(ScriptShell.Bash, "/tmp/s.sh")
            .Should().Equal("--noprofile", "--norc", "-eo", "pipefail", "/tmp/s.sh");
        ScriptShellSupport.BuildInterpreterArguments(ScriptShell.Sh, "/tmp/s.sh")
            .Should().Equal("-e", "/tmp/s.sh");
        ScriptShellSupport.BuildInterpreterArguments(ScriptShell.Pwsh, "/tmp/s.ps1")
            .Should().Equal("-NoLogo", "-NoProfile", "-NonInteractive", "-Command", ". '/tmp/s.ps1'");
        ScriptShellSupport.BuildInterpreterArguments(ScriptShell.PowerShell, "C:\\t\\s.ps1")
            .Should().Equal("-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Unrestricted", "-Command", ". 'C:\\t\\s.ps1'");
        ScriptShellSupport.BuildInterpreterArguments(ScriptShell.Python, "/tmp/s.py")
            .Should().Equal("/tmp/s.py");
        ScriptShellSupport.BuildInterpreterArguments(ScriptShell.Cmd, "C:\\t\\s.cmd")
            .Should().Equal("/d", "/s", "/c", "\"C:\\t\\s.cmd\"");
    }

    [Fact]
    public void BuildInterpreterArguments_PowerShell_EscapesSingleQuotesInPath()
    {
        var arguments = ScriptShellSupport.BuildInterpreterArguments(ScriptShell.Pwsh, "/tmp/it's.ps1");

        arguments[^1].Should().Be(". '/tmp/it''s.ps1'");
    }

    [Theory]
    [InlineData("Bash", true)]
    [InlineData("Bash", false)]
    [InlineData("Sh", true)]
    [InlineData("Pwsh", true)]
    [InlineData("Pwsh", false)]
    [InlineData("PowerShell", false)]
    [InlineData("Python", true)]
    [InlineData("Python", false)]
    [InlineData("Cmd", false)]
    public void GetInstallHint_IsNeverEmpty(string shellName, bool inContainer)
    {
        ScriptShellSupport.GetInstallHint(Enum.Parse<ScriptShell>(shellName), inContainer).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GetInstallHint_ContainerHintsMentionTheImage()
    {
        ScriptShellSupport.GetInstallHint(ScriptShell.Pwsh, inContainer: true).Should().Contain("mcr.microsoft.com/powershell");
        ScriptShellSupport.GetInstallHint(ScriptShell.Python, inContainer: true).Should().Contain("python:3.12");
        ScriptShellSupport.GetInstallHint(ScriptShell.Bash, inContainer: true).Should().Contain("shell: sh");
        ScriptShellSupport.GetInstallHint(ScriptShell.Cmd, inContainer: false).Should().Contain("Windows");
    }
}
