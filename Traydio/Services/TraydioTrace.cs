using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Traydio.Services;

/// <summary>
/// Lightweight tracing helper for operational diagnostics.
/// </summary>
public static class TraydioTrace
{
    private static readonly Lock _gate = new();
    private static ILoggerFactory? _loggerFactory;

    /// <summary>
    /// Initializes tracing with a logger factory resolved from DI.
    /// </summary>
    /// <param name="loggerFactory">Logger factory to use for future trace writes.</param>
    public static void Initialize(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        lock (_gate)
        {
            _loggerFactory = loggerFactory;
        }
    }

    /// <summary>
    /// Writes an informational trace entry.
    /// </summary>
    /// <param name="area">Logical subsystem name.</param>
    /// <param name="message">Trace message.</param>
    public static void Info(string area, string message)
        => Write("Info", area, message);

    /// <summary>
    /// Writes a debug trace entry.
    /// </summary>
    /// <param name="area">Logical subsystem name.</param>
    /// <param name="message">Trace message.</param>
    public static void Debug(string area, string message)
        => Write("Debug", area, message);

    /// <summary>
    /// Writes a warning trace entry.
    /// </summary>
    /// <param name="area">Logical subsystem name.</param>
    /// <param name="message">Trace message.</param>
    public static void Warn(string area, string message)
        => Write("Warn", area, message);

    /// <summary>
    /// Writes an error trace entry.
    /// </summary>
    /// <param name="area">Logical subsystem name.</param>
    /// <param name="message">Trace message.</param>
    /// <param name="exception">Optional related exception.</param>
    public static void Error(string area, string message, Exception? exception = null)
    {
        if (string.IsNullOrWhiteSpace(area) || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var logger = GetLogger(area);
        if (logger is not null)
        {
            logger.LogError(exception, "{Message}", message);
            return;
        }

        var suffix = exception is null ? string.Empty : " " + exception;
        Trace.WriteLine($"[Traydio][Error][{area}] {message}{suffix}");
    }

    private static void Write(string level, string area, string message)
    {
        if (string.IsNullOrWhiteSpace(area) || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var logger = GetLogger(area);
        if (logger is not null)
        {
            var formatted = "[" + level + "] " + message;
            switch (level)
            {
                case "Info":
                    logger.LogInformation("{Message}", formatted);
                    return;
                case "Warn":
                    logger.LogWarning("{Message}", formatted);
                    return;
                default:
                    logger.LogDebug("{Message}", formatted);
                    return;
            }
        }

        var line = $"[Traydio][{level}][{area}] {message}";
        Trace.WriteLine(line);
    }

    private static ILogger? GetLogger(string area)
    {
        lock (_gate)
        {
            return _loggerFactory?.CreateLogger("Traydio." + area);
        }
    }
}

