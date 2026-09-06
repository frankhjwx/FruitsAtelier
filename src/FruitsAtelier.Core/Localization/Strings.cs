using System.Globalization;

namespace FruitsAtelier.Localization;

public static class Strings
{
    private static readonly LocalizationCatalog catalog = LoadCatalog();
    private static string language = "en";
    private static readonly object messageLock = new();
    private static readonly Dictionary<string, (string Key, object?[] Args)> messages = new(StringComparer.Ordinal);
    private static readonly Queue<string> messageOrder = new();

    public static string Language => Volatile.Read(ref language);
    public static IReadOnlyList<string> AvailableLanguages => catalog.AvailableLanguages;
    public static event Action? LanguageChanged;
    public static IReadOnlyList<string> Validate() => catalog.Validate();

    public static string Get(string key, params object?[] args)
    {
        string value = catalog.Get(Language, key, args);
        lock (messageLock)
        {
            if (!messages.ContainsKey(value)) messageOrder.Enqueue(value);
            messages[value] = (key, (object?[])args.Clone());
            while (messages.Count > 2048) messages.Remove(messageOrder.Dequeue());
        }
        return value;
    }

    // Only retained UI messages are reformatted; argument strings can contain user data.
    public static string Reformat(string value)
    {
        (string Key, object?[] Args) message;
        lock (messageLock) { if (!messages.TryGetValue(value, out message)) return value; }
        return Get(message.Key, message.Args);
    }

    public static object Localized(string message)
    {
        lock (messageLock)
            return messages.TryGetValue(message, out var value) ? new MessageArgument(value.Key, value.Args) : message;
    }

    private sealed record MessageArgument(string Key, object?[] Args)
    {
        public override string ToString() => Get(Key, Args);
    }

    public static void SetLanguage(string value)
    {
        if (!AvailableLanguages.Contains(value)) throw new ArgumentOutOfRangeException(nameof(value));
        if (Interlocked.Exchange(ref language, value) != value) LanguageChanged?.Invoke();
    }

    private static LocalizationCatalog LoadCatalog()
    {
        const string prefix = "FruitsAtelier.Localization.";
        var tables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string resource in typeof(Strings).Assembly.GetManifestResourceNames().Where(name => name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal)))
        {
            using var stream = typeof(Strings).Assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            tables.Add(resource[prefix.Length..^5], reader.ReadToEnd());
        }
        return LocalizationCatalog.Parse(tables);
    }
}
