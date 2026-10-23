using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Mac;

internal sealed class MacOsuBrowser : NativeControlHost
{
    private string? target, navigated;
    private readonly TimestampCallback callback;
    public Action<string, string>? EditorLink;
    public MacOsuBrowser() { callback = (link, page) => Avalonia.Threading.Dispatcher.UIThread.Post(() => EditorLink?.Invoke(link, page)); }
    public BrowserState State
    {
        get { int flags = created ? StateNative() : 0; return new(PageUrl, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0, (flags & 8) != 0); }
    }
    public void Execute(BrowserCommand command, string? mapUrl) { if (created) CommandNative((int)command, mapUrl ?? ""); }
    public string? PageUrl => created ? Marshal.PtrToStringUTF8(UrlNative()) : null;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void TimestampCallback([MarshalAs(UnmanagedType.LPUTF8Str)] string link, [MarshalAs(UnmanagedType.LPUTF8Str)] string page);
    private bool created;

    public void Open(string url)
    {
        if (!OsuBrowser.IsWebsite(url) || target == url) return;
        target = url;
        var context = OsuBrowser.PageContext(PageUrl);
        var requested = OsuBrowser.PageContext(url);
        if (created && (context.Map <= 0 || context.Map != requested.Map || context.Set != requested.Set)) { NavigateNative(url); navigated = url; }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var labels = new Dictionary<string, string>();
        foreach (string key in new[] { "title", "back", "forward", "reload", "external", "loading", "failed", "ok", "cancel" })
            labels[key] = L.Get("browser." + key);
        nint handle = CreateNative(JsonSerializer.Serialize(labels), callback);
        if (handle == 0) throw new InvalidOperationException(L.Get("browser.failed"));
        created = true;
        if (target is { } url && navigated != url) { NavigateNative(url); navigated = url; }
        return new PlatformHandle(handle, "NSView");
    }

    // The retained native controller owns the view until the editor window closes.
    protected override void DestroyNativeControlCore(IPlatformHandle control) { created = false; }

    [DllImport("libFruitsAtelierAudio.dylib", EntryPoint = "fa_browser_create")]
    private static extern nint CreateNative([MarshalAs(UnmanagedType.LPUTF8Str)] string labels, TimestampCallback callback);

    [DllImport("libFruitsAtelierAudio.dylib", EntryPoint = "fa_browser_navigate")]
    private static extern void NavigateNative([MarshalAs(UnmanagedType.LPUTF8Str)] string url);

    [DllImport("libFruitsAtelierAudio.dylib", EntryPoint = "fa_browser_state")]
    private static extern int StateNative();
    [DllImport("libFruitsAtelierAudio.dylib", EntryPoint = "fa_browser_command")]
    private static extern void CommandNative(int command, [MarshalAs(UnmanagedType.LPUTF8Str)] string url);
    [DllImport("libFruitsAtelierAudio.dylib", EntryPoint = "fa_browser_url")]
    private static extern nint UrlNative();

    [DllImport("libFruitsAtelierAudio.dylib", EntryPoint = "fa_browser_close")]
    internal static extern void Close();

    private static TaskCompletionSource? clearCompletion;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ClearCallback();
    private static readonly ClearCallback cleared = () => clearCompletion?.TrySetResult();
    internal static Task ClearDataAsync()
    {
        clearCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ClearNative(cleared);
        return clearCompletion.Task;
    }
    [DllImport("libFruitsAtelierAudio.dylib", EntryPoint = "fa_browser_clear_data")]
    private static extern void ClearNative(ClearCallback callback);
}
