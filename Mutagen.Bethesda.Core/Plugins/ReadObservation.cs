using System.Threading;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins;

/// <summary>An execution-scoped, additive observation hook. No observer is installed during normal parsing.</summary>
public static class ReadObservation
{
    private static readonly AsyncLocal<Action<string, ModKey, string>?> Observer = new();
    [ThreadStatic] private static bool _reporting;
    public static IDisposable Begin(Action<string, ModKey, string> observer)
    {
        var previous = Observer.Value;
        Observer.Value = observer;
        return new Scope(previous);
    }
    public static IDisposable Suspend()
    {
        var previous = Observer.Value;
        Observer.Value = null;
        return new Scope(previous);
    }
    public static void Report(string kind, ModKey plugin, string key)
    {
        if (kind is "record" or "resolve" or "versions" && key == "Null") return;
        var observer = Observer.Value;
        if (observer is null || _reporting) return;
        try { _reporting = true; observer(kind, plugin, key); }
        finally { _reporting = false; }
    }
    public static FormKey Form(ModKey plugin, FormKey key)
    {
        Report("record", plugin, key.ToString());
        return key;
    }
    internal static void Record(ModKey plugin, object record)
    {
        if (Observer.Value is null || _reporting) return;
        try
        {
            _reporting = true;
            if (record is IMajorRecordGetter major) Observer.Value("record", plugin, major.FormKey.ToString());
        }
        finally { _reporting = false; }
    }
    private sealed class Scope(Action<string, ModKey, string>? previous) : IDisposable
    {
        public void Dispose() => Observer.Value = previous;
    }
}
