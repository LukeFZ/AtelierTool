using Spectre.Console;

namespace AtelierTool;

public static class ConsoleLogger
{
    public static void WriteInfoLine(string line)
    {
        AnsiConsole.MarkupLine($"[white bold]INFO:[/] {line}");
    }

    public static void WriteWarnLine(string line)
    {
        AnsiConsole.MarkupLine($"[orange1 bold]WARN:[/] {line}");
    }

    public static void WriteErrLine(string line)
    {
        AnsiConsole.MarkupLine($"[red bold]ERR:[/] {line}");
    }
}