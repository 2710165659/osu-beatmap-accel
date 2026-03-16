using System;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.ProxyAccel;

public static class ProxyLogging
{
    private const string prefix = "ProxyAccel";

    public static void Log(string? message, LogLevel level = LogLevel.Verbose)
        => Logger.Log($"[{prefix}] {message}", level: level, target: LoggingTarget.Runtime);

    public static void LogError(Exception exception, string? message = null, LogLevel level = LogLevel.Important)
    {
        Exception current = exception;
        string? currentMessage = message;

        while (true)
        {
            Logger.Log($"[{prefix}] {(string.IsNullOrEmpty(currentMessage) ? string.Empty : $"{currentMessage}: ")}{current.Message}", level: level);
            Logger.Log(current.StackTrace, level: LogLevel.Verbose);

            if (current.InnerException == null)
                break;

            current = current.InnerException;
            currentMessage = null;
        }
    }
}
