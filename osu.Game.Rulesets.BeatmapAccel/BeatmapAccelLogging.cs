using System;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.BeatmapAccel;

public static class BeatmapAccelLogging
{
    private const string prefix = "BeatmapAccel";

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

    /// <summary>
    /// 构造 "OuterType -> InnerType -> ..." 形式的异常类型链，用于诊断传输中断的确切来源。
    /// </summary>
    public static string BuildExceptionTypeChain(Exception exception)
    {
        string chain = exception.GetType().FullName ?? exception.GetType().Name;

        for (Exception? inner = exception.InnerException; inner != null; inner = inner.InnerException)
            chain += " -> " + (inner.GetType().FullName ?? inner.GetType().Name);

        return chain;
    }
}
