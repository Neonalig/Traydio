using System;
using System.Diagnostics;

namespace Traydio.Services;

/// <summary>
/// Lightweight tracing helper for operational diagnostics.
/// </summary>
public static class TraydioTrace
{
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

    private static void Write(string level, string area, string message)
    {
        if (string.IsNullOrWhiteSpace(area) || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var line = $"[Traydio][{level}][{area}] {message}";
        Trace.WriteLine(line);
    }
}

