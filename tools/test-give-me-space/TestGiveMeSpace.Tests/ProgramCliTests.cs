using System.Text.Json;
using TestGiveMeSpace.App;
using TestGiveMeSpace.Core;

namespace TestGiveMeSpace.Tests;

public sealed class ProgramCliTests
{
    [Fact]
    public void Main_without_arguments_writes_help_and_succeeds()
    {
        ProgramResult result = RunProgram();

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("Использование:", result.Stdout);
        Assert.Contains("request", result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    public void Main_help_aliases_write_help_and_succeed(string arg)
    {
        ProgramResult result = RunProgram(arg);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("Использование:", result.Stdout);
        Assert.Contains("--purpose", result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Theory]
    [InlineData("request", "--help")]
    [InlineData("status", "-h")]
    public void Main_subcommand_help_aliases_write_help_and_succeed(string command, string helpArg)
    {
        ProgramResult result = RunProgram(command, helpArg);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("Использование:", result.Stdout);
        Assert.Contains(command, result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public void Main_help_describes_plaque_relocation_commands()
    {
        ProgramResult result = RunProgram("--help");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("avoid-point --x <screen-x> --y <screen-y> --owner <id>", result.Stdout);
        Assert.Contains("restore-position --owner <id>", result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("-V")]
    public void Main_version_aliases_write_version_and_succeed(string arg)
    {
        ProgramResult result = RunProgram(arg);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.StartsWith("test-give-me-space ", result.Stdout.Trim());
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public void Main_invalid_command_writes_json_to_stdout_and_message_to_stderr()
    {
        ProgramResult result = RunProgram("wat");

        Assert.Equal(ExitCodes.ProtocolError, result.ExitCode);
        Assert.Contains("Некорректная команда", result.Stderr);

        using JsonDocument doc = JsonDocument.Parse(result.Stdout);
        Assert.Equal("protocol_error", doc.RootElement.GetProperty("status").GetString());
        Assert.Contains(
            "Некорректная команда",
            doc.RootElement.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("avoid-point", "--x", "1", "--owner", "chat-1")]
    [InlineData("avoid-point", "--x", "nope", "--y", "2", "--owner", "chat-1")]
    [InlineData("restore-position", "--x", "1", "--owner", "chat-1")]
    public void Main_rejects_invalid_relocation_arguments(params string[] args)
    {
        ProgramResult result = RunProgram(args);

        Assert.Equal(ExitCodes.ProtocolError, result.ExitCode);
        Assert.Contains("Некоррект", result.Stderr);
        using JsonDocument doc = JsonDocument.Parse(result.Stdout);
        Assert.Equal("protocol_error", doc.RootElement.GetProperty("status").GetString());
    }

    private static ProgramResult RunProgram(params string[] args)
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using StringWriter stdout = new();
        using StringWriter stderr = new();

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);

            int exitCode = Program.Main(args);
            return new ProgramResult(exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private sealed record ProgramResult(int ExitCode, string Stdout, string Stderr);
}
