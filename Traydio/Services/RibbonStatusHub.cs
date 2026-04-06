using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Traydio.Services;

public static class RibbonStatusHub
{
    private static readonly Lock _gate = new();
    private static readonly Dictionary<string, RibbonStatusEntry> _entries = new(StringComparer.Ordinal);
    private static long _sequence;

    public static event EventHandler? Changed;

    public static string GetCurrentText(string fallback)
    {
        lock (_gate)
        {
            return GetTopEntry_NoLock()?.Text ?? fallback;
        }
    }

    public static void SetOverride(string id, string text, int priority)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        lock (_gate)
        {
            _entries[id.Trim()] = new RibbonStatusEntry(text.Trim(), priority, ++_sequence);
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void RemoveOverride(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var removed = false;
        lock (_gate)
        {
            removed = _entries.Remove(id.Trim());
        }

        if (removed)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    public static void SetTemporaryOverride(string id, string text, int priority, TimeSpan duration)
    {
        SetOverride(id, text, priority);

        ClearTemporaryOverrideAsync(id, duration)
            .ForgetWithErrorHandling("Clear temporary ribbon status", showDialog: false);
    }

    private static async Task ClearTemporaryOverrideAsync(string id, TimeSpan duration)
    {
        await Task.Delay(duration).ConfigureAwait(false);
        RemoveOverride(id);
    }

    private static RibbonStatusEntry? GetTopEntry_NoLock()
    {
        RibbonStatusEntry? top = null;
        foreach (var entry in _entries.Values)
        {
            if (top is null
                || entry.Priority > top.Priority
                || (entry.Priority == top.Priority && entry.Sequence > top.Sequence))
            {
                top = entry;
            }
        }

        return top;
    }

    private sealed record RibbonStatusEntry(string Text, int Priority, long Sequence);
}


