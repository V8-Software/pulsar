using System.IO;
using System.Reflection;
using System.Threading;
using TestGiveMeSpace.Core;

namespace TestGiveMeSpace.App;

public static class Program
{
    private const string AppName = "test-give-me-space";
    private static readonly TimeSpan PipeConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PipeReadTimeout = TimeSpan.FromSeconds(5);

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            GuardAppPaths paths = GuardAppPaths.Create();
            if (IsHelpRequest(args))
            {
                Console.Out.Write(HelpText);
                return ExitCodes.Success;
            }

            if (IsVersionRequest(args))
            {
                Console.Out.WriteLine($"{AppName} {GetVersion()}");
                return ExitCodes.Success;
            }

            if (!TryParseCommand(
                args,
                out GuardRequest? request))
            {
                return WriteResponse(
                    GuardResponse.FromStatus(GuardStatus.ProtocolError, BuildInvalidCommandMessage(args)),
                    writeMessageToError: true);
            }

            string executablePath = Environment.ProcessPath
                ?? Environment.GetCommandLineArgs().First();
            GuardCommandRunner runner = new(
                new GuardPipeClient(paths.PipeName, PipeConnectTimeout, PipeReadTimeout),
                new ServerProcess(executablePath),
                new StateStore(paths.StatePath));

            GuardResponse response = runner.ExecuteAsync(request!, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            return WriteResponse(response);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return WriteResponse(
                GuardResponse.FromStatus(GuardStatus.IoError),
                writeMessageToError: true);
        }
    }

    private static bool TryParseCommand(string[] args, out GuardRequest? request)
    {
        request = null;
        if (args.Length == 0
            || !GuardCommandExtensions.TryParseWireValue(args[0], out GuardCommand command))
        {
            return false;
        }

        if (command == GuardCommand.AvoidPoint)
        {
            return TryParseAvoidPoint(args, out request);
        }

        if (command == GuardCommand.RestorePosition)
        {
            return TryParseOwnerOnlyCommand(args, out request, GuardCommand.RestorePosition);
        }

        if (command == GuardCommand.Request)
        {
            return TryParseRequest(args, out request);
        }

        if (command is GuardCommand.Finish or GuardCommand.Cancel or GuardCommand.Hide or GuardCommand.Show)
        {
            return TryParseOwnerOnlyCommand(args, out request, command);
        }

        return args.Length == 1 && (request = GuardRequest.ForCommand(command)) is not null;
    }

    private static bool TryParseAvoidPoint(string[] args, out GuardRequest? request)
    {
        request = null;
        int? x = null;
        int? y = null;
        string? owner = null;
        for (int i = 1; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length)
            {
                return false;
            }

            switch (args[i])
            {
                case "--x":
                    if (!int.TryParse(args[i + 1], out int parsedX))
                    {
                        return false;
                    }

                    x = parsedX;
                    break;
                case "--y":
                    if (!int.TryParse(args[i + 1], out int parsedY))
                    {
                        return false;
                    }

                    y = parsedY;
                    break;
                case "--owner":
                    if (string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        return false;
                    }

                    owner = args[i + 1].Trim();
                    break;
                default:
                    return false;
            }
        }

        if (!x.HasValue || !y.HasValue)
        {
            return false;
        }

        owner ??= ResolveDefaultOwner();
        request = new GuardRequest(GuardCommand.AvoidPoint, GuardPurpose.Test, owner, x, y);
        return true;
    }

    private static bool TryParseOwnerOnlyCommand(
        string[] args,
        out GuardRequest? request,
        GuardCommand command)
    {
        request = null;
        string? owner = null;
        for (int i = 1; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i] != "--owner")
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(args[i + 1]))
            {
                return false;
            }

            owner = args[i + 1].Trim();
        }

        owner ??= ResolveDefaultOwner();
        request = new GuardRequest(command, GuardPurpose.Test, owner);
        return true;
    }

    private static bool TryParseRequest(string[] args, out GuardRequest? request)
    {
        request = null;
        GuardPurpose purpose = GuardPurpose.Test;
        string? owner = null;
        for (int i = 1; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length)
            {
                return false;
            }

            switch (args[i])
            {
                case "--purpose":
                    if (!GuardPurposeExtensions.TryParseWireValue(args[i + 1], out purpose))
                    {
                        return false;
                    }

                    break;
                case "--owner":
                    if (string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        return false;
                    }

                    owner = args[i + 1].Trim();
                    break;
                default:
                    return false;
            }
        }

        owner ??= ResolveDefaultOwner();
        request = new GuardRequest(GuardCommand.Request, purpose, owner);
        return true;
    }

    private static int WriteResponse(GuardResponse response, bool writeMessageToError = false)
    {
        if (writeMessageToError && !string.IsNullOrWhiteSpace(response.Message))
        {
            Console.Error.WriteLine(response.Message);
        }

        Console.Out.WriteLine(response.ToJson());
        return response.ExitCode;
    }

    private static bool IsHelpRequest(string[] args)
    {
        if (args.Length == 0)
        {
            return true;
        }

        if (args.Length == 1)
        {
            return IsHelpAlias(args[0]);
        }

        if (args.Length == 2
            && (IsCommandName(args[0]) || args[0] == "help")
            && IsHelpAlias(args[1]))
        {
            return true;
        }

        return args.Length == 2
            && args[0] == "help"
            && IsCommandName(args[1]);
    }

    private static bool IsVersionRequest(string[] args)
        => args is ["--version"] or ["-V"];

    private static bool IsHelpAlias(string value)
        => value is "--help" or "-h" or "help";

    private static string BuildInvalidCommandMessage(string[] args)
    {
        if (args.Length > 0 && !IsCommandName(args[0]))
        {
            return $"Некорректная команда `{args[0]}`. Используйте `{AppName} --help`.";
        }

        return $"Некорректные параметры команды. Используйте `{AppName} --help`.";
    }

    private static bool IsCommandName(string value)
        => GuardCommandExtensions.TryParseWireValue(value, out _);

    private static string GetVersion()
    {
        Assembly assembly = typeof(Program).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return assembly.GetName().Version?.ToString(fieldCount: 3) ?? "0.0.0";
    }

    private static string? ResolveDefaultOwner()
    {
        string? explicitOwner = Environment.GetEnvironmentVariable("TEST_GIVE_ME_SPACE_OWNER");
        if (!string.IsNullOrWhiteSpace(explicitOwner))
        {
            return explicitOwner.Trim();
        }

        string? codexThreadId = Environment.GetEnvironmentVariable("CODEX_THREAD_ID");
        return string.IsNullOrWhiteSpace(codexThreadId)
            ? null
            : codexThreadId.Trim();
    }

    private const string HelpText = """
test-give-me-space — плашка-предупреждение перед тестами с живым рабочим столом.

Использование:
  test-give-me-space request [--purpose test|observe-windows] [--owner <id>]
  test-give-me-space status
  test-give-me-space finish [--owner <id>]
  test-give-me-space cancel [--owner <id>]
  test-give-me-space hide --owner <id>
  test-give-me-space show --owner <id>
  test-give-me-space avoid-point --x <screen-x> --y <screen-y> --owner <id>
  test-give-me-space restore-position --owner <id>
  test-give-me-space --help
  test-give-me-space --version

Команды:
  request   показать плашку с отсчётом и дождаться разрешения
  status    вернуть текущее состояние
  finish    штатно закрыть плашку; звук подаётся только после минуты работы
  cancel    сбросить состояние без звукового сигнала
  hide      временно скрыть плашки текущего теста
  show      снова показать временно скрытые плашки
  avoid-point       временно убрать плашку, закрывающую указанную точку экрана
  restore-position  вернуть временно перемещённую плашку

Флаги:
  --purpose test              тексты для тестирования, используется по умолчанию
  --purpose observe-windows   тексты для изучения состояния окон
  --owner <id>                идентификатор агента или чата
  -h, --help                  показать эту справку
  --version, -V               показать версию

Вывод:
  Служебные команды пишут JSON в stdout. Ошибки параметров дополнительно пишутся в stderr.

""";
}
